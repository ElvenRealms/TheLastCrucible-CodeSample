using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Binds an audio page's volume sliders to the audio settings and keeps each slider's percentage
/// label current. The page names its sliders master, music, sfx, ambience and ui, each with a
/// matching "&lt;name&gt;Value" label.
/// </summary>
public class AudioSettingsSliders
{
    private readonly List<Action> _unbind = new();
    private readonly VisualElement _page;
    private readonly AudioSettingsController _audioSettings;
    private readonly SliderInt _master;
    private readonly SliderInt _music;
    private readonly SliderInt _sfx;
    private readonly SliderInt _ambience;
    private readonly SliderInt _ui;

    /// <summary>Raised after the player moves a slider. <see cref="Sync"/> does not raise it.</summary>
    public event Action Changed;

    public AudioSettingsSliders(VisualElement page, AudioSettingsController audioSettings)
    {
        _page = page;
        _audioSettings = audioSettings;
        _master = Bind("master", audioSettings.SetMasterVolume);
        _music = Bind("music", value => audioSettings.SetVolume(AudioCategory.Music, value));
        _sfx = Bind("sfx", value => audioSettings.SetVolume(AudioCategory.SFX, value));
        _ambience = Bind("ambience", value => audioSettings.SetVolume(AudioCategory.Ambience, value));
        _ui = Bind("ui", value => audioSettings.SetVolume(AudioCategory.UI, value));
    }

    /// <summary>Shows the saved volumes on the sliders and their labels.</summary>
    public void Sync()
    {
        Show(_master, _audioSettings.MasterVolume);
        Show(_music, _audioSettings.MusicVolume);
        Show(_sfx, _audioSettings.SfxVolume);
        Show(_ambience, _audioSettings.AmbienceVolume);
        Show(_ui, _audioSettings.UiVolume);
    }

    public void FocusFirst()
    {
        _master.Focus();
    }

    public void Unbind()
    {
        foreach (var unbind in _unbind)
        {
            unbind();
        }
        _unbind.Clear();
    }

    private SliderInt Bind(string name, Action<float> apply)
    {
        var slider = _page.Q<SliderInt>(name);
        EventCallback<ChangeEvent<int>> callback = evt =>
        {
            apply(evt.newValue / 100f);
            ShowValueLabel(slider);
            Changed?.Invoke();
        };
        slider.RegisterValueChangedCallback(callback);
        _unbind.Add(() => slider.UnregisterValueChangedCallback(callback));
        return slider;
    }

    private void Show(SliderInt slider, float value)
    {
        slider.SetValueWithoutNotify(Mathf.RoundToInt(value * 100f));
        ShowValueLabel(slider);
    }

    private void ShowValueLabel(SliderInt slider)
    {
        _page.Q<Label>(slider.name + "Value").text = slider.value + "%";
    }
}
