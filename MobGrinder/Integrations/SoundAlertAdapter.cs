using FFXIVClientStructs.FFXIV.Client.UI;

namespace MobGrinder;

/// <summary>播放游戏内置的固定聊天音效。</summary>
public sealed unsafe class SoundAlertAdapter
{
    public void Play(uint soundEffectId)
    {
        if (soundEffectId is < 1 or > 16)
            return;

        UIGlobals.PlayChatSoundEffect(soundEffectId);
    }
}
