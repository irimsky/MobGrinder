using Dalamud.Game.Command;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace MobGrinder;

public sealed class Plugin : IAsyncDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly MobGrinderConfiguration configuration;
    private readonly MobSpawnDataService spawnData;
    private readonly MobGrinderController controller;
    private readonly MobGrinderIpcProvider ipcProvider;
    private readonly MobGrinderWindow window;
    private readonly MobGrinderOverlayWindow overlayWindow;
    private readonly MobGrinderLogWindow logWindow;
    private readonly MobGrinderSettingsWindow settingsWindow;
    private readonly IFontHandle uiFont;
    private readonly WindowSystem windowSystem = new("MobGrinder");

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        ICommandManager commandManager,
        IFramework framework,
        IClientState clientState,
        IPlayerState playerState,
        IUnlockState unlockState,
        IObjectTable objectTable,
        ITargetManager targetManager,
        ICondition condition,
        IPartyList partyList,
        IDataManager dataManager,
        IAetheryteList aetheryteList,
        IGameInventory gameInventory,
        IGameInteropProvider gameInteropProvider)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.log = log;

        MobGrinderController? controller = null;
        LandingAdapter? landing = null;
        bool drawRegistered = false;
        bool mainUiRegistered = false;
        bool configUiRegistered = false;
        bool primaryCommandRegistered = false;
        bool shortCommandRegistered = false;
        MobGrinderWindow? window = null;
        MobGrinderOverlayWindow? overlayWindow = null;
        MobGrinderSettingsWindow? settingsWindow = null;
        MobGrinderIpcProvider? ipcProvider = null;
        IFontHandle? uiFont = null;

        try
        {
            this.configuration = pluginInterface.GetPluginConfig() as MobGrinderConfiguration
                ?? new MobGrinderConfiguration();
            this.configuration.Migrate();
            this.configuration.Normalize();
            this.configuration.Enabled = false;

            MobCoordinateService coordinates = new(dataManager, log);
            this.spawnData = new MobSpawnDataService(log, dataManager, coordinates);
            VnavmeshIpc vnavmesh = new(pluginInterface);
            LifestreamIpc lifestream = new(pluginInterface);
            AetheryteTravelPlanner travelPlanner = new(dataManager, aetheryteList);
            InventoryCounter inventoryCounter = new(gameInventory);
            MountAdapter mount = new(condition, objectTable);
            landing = new LandingAdapter(gameInteropProvider);
            FieldNavigation.NoFlyZoneCatalog noFlyZones = new(dataManager);
            SoundAlertAdapter soundAlerts = new();
            NativeGameStateAdapter nativeGameState = new();

            controller = new MobGrinderController(
                this.configuration,
                log,
                framework,
                clientState,
                playerState,
                objectTable,
                targetManager,
                condition,
                partyList,
                this.spawnData,
                vnavmesh,
                lifestream,
                travelPlanner,
                inventoryCounter,
                mount,
                landing,
                soundAlerts,
                noFlyZones,
                nativeGameState,
                new BeastmasterGameAdapter(dataManager, unlockState));
            ipcProvider = new MobGrinderIpcProvider(this.pluginInterface, controller, log);

            MobGrinderLogWindow logWindow = new(controller);
            this.uiFont = uiFont = pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(
                step => step.OnPreBuild(toolkit => toolkit.AddDalamudDefaultFont(UiBuilder.DefaultFontSizePx + 2f)));
            window = new(
                this.configuration,
                controller,
                this.SaveConfiguration,
                logWindow.Open,
                visible => overlayWindow?.SetVisible(visible),
                () => settingsWindow?.Open());
            this.settingsWindow = settingsWindow = new(window.DrawSettingsPage);
            overlayWindow = new(this.pluginInterface, this.configuration, window);
            this.windowSystem.AddWindow(window);
            this.windowSystem.AddWindow(overlayWindow);
            this.windowSystem.AddWindow(logWindow);
            this.windowSystem.AddWindow(settingsWindow);

            this.pluginInterface.UiBuilder.Draw += this.DrawUi;
            drawRegistered = true;
            this.pluginInterface.UiBuilder.OpenMainUi += window.Open;
            mainUiRegistered = true;
            this.pluginInterface.UiBuilder.OpenConfigUi += this.OpenSettingsWindow;
            configUiRegistered = true;

            CommandInfo command = new(this.OnCommand)
            {
                HelpMessage = "打开 MobGrinder；start|pause|stop|scan|status|log|ui|config",
                ShowInHelp = true,
            };
            if (!this.commandManager.AddHandler("/mobgrinder", command))
                throw new InvalidOperationException("命令 /mobgrinder 已被其他插件注册");
            primaryCommandRegistered = true;
            if (!this.commandManager.AddHandler("/mg", command))
                throw new InvalidOperationException("命令 /mg 已被其他插件注册");
            shortCommandRegistered = true;

            // Hook activation is last so every later failure can dispose an inactive hook.
            landing.Enable();

            this.controller = controller!;
            this.ipcProvider = ipcProvider!;
            this.window = window!;
            this.overlayWindow = overlayWindow!;
            this.logWindow = logWindow;
            this.log.Information("MobGrinder 插件已加载");
        }
        catch
        {
            if (shortCommandRegistered)
                this.commandManager.RemoveHandler("/mg");
            if (primaryCommandRegistered)
                this.commandManager.RemoveHandler("/mobgrinder");
            if (configUiRegistered && window is not null)
                this.pluginInterface.UiBuilder.OpenConfigUi -= this.OpenSettingsWindow;
            if (mainUiRegistered && window is not null)
                this.pluginInterface.UiBuilder.OpenMainUi -= window.Open;
            if (drawRegistered)
                this.pluginInterface.UiBuilder.Draw -= this.DrawUi;
            this.windowSystem.RemoveAllWindows();
            uiFont?.Dispose();
            ipcProvider?.Dispose();
            controller?.Dispose();
            landing?.Dispose();
            throw;
        }
    }

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        this.commandManager.RemoveHandler("/mobgrinder");
        this.commandManager.RemoveHandler("/mg");
        this.pluginInterface.UiBuilder.Draw -= this.DrawUi;
        this.pluginInterface.UiBuilder.OpenMainUi -= this.window.Open;
        this.pluginInterface.UiBuilder.OpenConfigUi -= this.OpenSettingsWindow;
        this.windowSystem.RemoveAllWindows();
        this.uiFont.Dispose();
        this.ipcProvider.Dispose();
        await this.controller.DisposeAsync().ConfigureAwait(false);
        this.SaveConfiguration();
    }

    private void OnCommand(string _, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
            case "ui":
                this.window.Open();
                break;
            case "config":
                this.OpenSettingsWindow();
                break;
            case "start":
                this.controller.Start();
                break;
            case "pause":
                this.controller.Pause();
                break;
            case "stop":
                this.controller.Stop();
                break;
            case "scan":
                this.controller.ScanNow();
                break;
            case "status":
                this.log.Information(
                    "MobGrinder 状态：{State}；{Reason}；周边野怪={Count}",
                    this.controller.State,
                    this.controller.StatusReason,
                    this.controller.Mobs.Count);
                break;
            case "log":
                this.logWindow.Open();
                break;
            default:
                this.log.Warning("未知 MobGrinder 命令：{Command}；可用命令：start、pause、stop、scan、status、log、ui、config", args.Trim());
                break;
        }
    }

    private void OpenSettingsWindow() => this.settingsWindow.Open();

    private void DrawUi()
    {
        using (this.uiFont.Push())
            this.windowSystem.Draw();
    }

    private void SaveConfiguration() => this.pluginInterface.SavePluginConfig(this.configuration);
}
