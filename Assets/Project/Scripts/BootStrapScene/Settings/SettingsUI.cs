using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SettingsUI : MonoBehaviour
{
    [Header("UI Refs")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;

    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private TMP_Dropdown fpsDropdown;
    [SerializeField] private Toggle vsyncToggle;

    private readonly int[] fpsOptions = new int[] { 0, 30, 60, 90, 120, 144, 165, 240, 360 };

    void OnEnable()
    {
        if (SettingsManager.I != null) InitFromSettings();
        WireEvents();
    }

    void OnDisable() => UnwireEvents();

    private static int HzInt(Resolution r)
    {
#if UNITY_2022_2_OR_NEWER
        uint num = r.refreshRateRatio.numerator;
        uint den = r.refreshRateRatio.denominator > 0 ? r.refreshRateRatio.denominator : 1u;
        return Mathf.RoundToInt((float)num / den);
#else
        return r.refreshRate;
#endif
    }

    private void InitFromSettings()
    {
        var sm = SettingsManager.I;

        if (masterSlider) masterSlider.SetValueWithoutNotify(sm.Data.master);
        if (musicSlider) musicSlider.SetValueWithoutNotify(sm.Data.music);
        if (sfxSlider) sfxSlider.SetValueWithoutNotify(sm.Data.sfx);

        if (vsyncToggle) vsyncToggle.SetIsOnWithoutNotify(sm.Data.vsync);

        if (resolutionDropdown)
        {
            var res = sm.GetResolutions();
            var options = new List<TMP_Dropdown.OptionData>();
            foreach (var r in res)
                options.Add(new TMP_Dropdown.OptionData($"{r.width} x {r.height} @{HzInt(r)}"));

            resolutionDropdown.ClearOptions();
            resolutionDropdown.AddOptions(options);
            resolutionDropdown.SetValueWithoutNotify(sm.GetResolutionIndexClosest());
        }

        if (fpsDropdown)
        {
            var options = new List<TMP_Dropdown.OptionData>();
            foreach (var cap in fpsOptions)
                options.Add(new TMP_Dropdown.OptionData(cap == 0 ? "Uncapped" : cap.ToString()));
            fpsDropdown.ClearOptions();
            fpsDropdown.AddOptions(options);

            int idx = 0;
            for (int i = 0; i < fpsOptions.Length; i++) if (fpsOptions[i] == sm.Data.fpsCap) { idx = i; break; }
            fpsDropdown.SetValueWithoutNotify(idx);
            fpsDropdown.interactable = !sm.Data.vsync;
        }
    }

    private void WireEvents()
    {
        if (masterSlider) masterSlider.onValueChanged.AddListener(v => SettingsManager.I?.SetMaster(v));
        if (musicSlider) musicSlider.onValueChanged.AddListener(v => SettingsManager.I?.SetMusic(v));
        if (sfxSlider) sfxSlider.onValueChanged.AddListener(v => SettingsManager.I?.SetSfx(v));
        if (vsyncToggle) vsyncToggle.onValueChanged.AddListener(on => { SettingsManager.I?.SetVSync(on); if (fpsDropdown) fpsDropdown.interactable = !on; });
        if (resolutionDropdown) resolutionDropdown.onValueChanged.AddListener(i => SettingsManager.I?.SetResolutionByIndex(i));
        if (fpsDropdown) fpsDropdown.onValueChanged.AddListener(i =>
        {
            var caps = new int[] { 0, 30, 60, 90, 120, 144, 165, 240, 360 };
            int cap = (i >= 0 && i < caps.Length) ? caps[i] : 0;
            SettingsManager.I?.SetFpsCap(cap);
        });
    }

    private void UnwireEvents()
    {
        if (masterSlider) masterSlider.onValueChanged.RemoveAllListeners();
        if (musicSlider) musicSlider.onValueChanged.RemoveAllListeners();
        if (sfxSlider) sfxSlider.onValueChanged.RemoveAllListeners();
        if (vsyncToggle) vsyncToggle.onValueChanged.RemoveAllListeners();
        if (resolutionDropdown) resolutionDropdown.onValueChanged.RemoveAllListeners();
        if (fpsDropdown) fpsDropdown.onValueChanged.RemoveAllListeners();
    }
}
