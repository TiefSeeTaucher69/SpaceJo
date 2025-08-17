using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[DefaultExecutionOrder(-5000)]
public class SettingsManager : MonoBehaviour
{
    public static SettingsManager I { get; private set; }

    [Header("Audio (optional)")]
    [SerializeField] private AudioMixer masterMixer;
    [SerializeField] private string masterParam = "MasterVolume";
    [SerializeField] private string musicParam = "MusicVolume";
    [SerializeField] private string sfxParam = "SFXVolume";

    [Header("Defaults")]
    [Range(0f, 1f)] public float defaultMaster = 1f;
    [Range(0f, 1f)] public float defaultMusic = 1f;
    [Range(0f, 1f)] public float defaultSfx = 1f;
    public bool defaultVSync = true;
    public int defaultFpsCap = 60;
    public int defaultWidth = 0;
    public int defaultHeight = 0;
    public int defaultRefresh = 0;

    [Serializable]
    public class SettingsData
    {
        public float master = 1f, music = 1f, sfx = 1f;
        public bool vsync = true;
        public int fpsCap = 60;
        public int width, height, refresh; // Hz (gerundet)
    }

    public SettingsData Data { get; private set; } = new();

    private List<Resolution> _uniqueResolutions;
    const string PREFS_KEY = "game_settings_v1";

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        BuildResolutionsList();
        Load();
        ApplyAll();
    }

    // ---- Helpers ----
    public static int ToHzInt(Resolution r)
    {
#if UNITY_2022_2_OR_NEWER
        uint num = r.refreshRateRatio.numerator;
        uint den = r.refreshRateRatio.denominator > 0 ? r.refreshRateRatio.denominator : 1u;
        return Mathf.RoundToInt((float)num / den);
#else
        return r.refreshRate;
#endif
    }

    // ---- Public API ----
    public void SetMaster(float v) { Data.master = Mathf.Clamp01(v); ApplyAudio(); Save(); }
    public void SetMusic(float v) { Data.music = Mathf.Clamp01(v); ApplyAudio(); Save(); }
    public void SetSfx(float v) { Data.sfx = Mathf.Clamp01(v); ApplyAudio(); Save(); }
    public void SetVSync(bool on) { Data.vsync = on; ApplyGraphics(); Save(); }
    public void SetFpsCap(int cap) { Data.fpsCap = Mathf.Max(0, cap); ApplyGraphics(); Save(); }

    public void SetResolutionByIndex(int idx)
    {
        if (_uniqueResolutions == null || _uniqueResolutions.Count == 0) BuildResolutionsList();
        idx = Mathf.Clamp(idx, 0, _uniqueResolutions.Count - 1);
        var r = _uniqueResolutions[idx];
        Data.width = r.width;
        Data.height = r.height;
        Data.refresh = ToHzInt(r);  // gerundet speichern
        ApplyResolution(); Save();
    }

    public int GetResolutionIndexClosest()
    {
        if (_uniqueResolutions == null || _uniqueResolutions.Count == 0) BuildResolutionsList();
        int best = 0; int bestScore = int.MaxValue;
        for (int i = 0; i < _uniqueResolutions.Count; i++)
        {
            var r = _uniqueResolutions[i];
            int score = Mathf.Abs(r.width - Data.width)
                      + Mathf.Abs(r.height - Data.height)
                      + Mathf.Abs(ToHzInt(r) - Data.refresh); // gerundet vergleichen
            if (score < bestScore) { best = i; bestScore = score; }
        }
        return best;
    }

    public IReadOnlyList<Resolution> GetResolutions() => _uniqueResolutions;

    // ---- Apply ----
    public void ApplyAll() { ApplyAudio(); ApplyResolution(); ApplyGraphics(); }

    private void ApplyAudio()
    {
        if (masterMixer && !string.IsNullOrEmpty(masterParam))
            masterMixer.SetFloat(masterParam, LinearToDb(Data.master));
        else
            AudioListener.volume = Data.master;

        if (masterMixer)
        {
            if (!string.IsNullOrEmpty(musicParam)) masterMixer.SetFloat(musicParam, LinearToDb(Data.music));
            if (!string.IsNullOrEmpty(sfxParam)) masterMixer.SetFloat(sfxParam, LinearToDb(Data.sfx));
        }
    }

    private void ApplyResolution()
    {
        if (Data.width == 0 || Data.height == 0)
        {
            if (defaultWidth > 0 && defaultHeight > 0)
            {
                Data.width = defaultWidth; Data.height = defaultHeight; Data.refresh = defaultRefresh;
            }
            else
            {
                var r = Screen.currentResolution;
#if UNITY_2022_2_OR_NEWER
                Data.width = r.width; Data.height = r.height; Data.refresh = ToHzInt(r);
#else
                Data.width = r.width; Data.height = r.height; Data.refresh = r.refreshRate;
#endif
            }
        }

        var mode = Screen.fullScreenMode;
        // Wenn du auch den Refresh aktiv setzen willst, nutze die 4-Parameter-Überladung:
        // Screen.SetResolution(Data.width, Data.height, mode, Data.refresh);
        Screen.SetResolution(Data.width, Data.height, mode);
    }

    private void ApplyGraphics()
    {
        QualitySettings.vSyncCount = Data.vsync ? 1 : 0;
        Application.targetFrameRate = Data.vsync ? -1 : (Data.fpsCap <= 0 ? -1 : Data.fpsCap);
    }

    // ---- Save/Load ----
    private void Load()
    {
        if (PlayerPrefs.HasKey(PREFS_KEY))
        {
            try { Data = JsonUtility.FromJson<SettingsData>(PlayerPrefs.GetString(PREFS_KEY)) ?? new SettingsData(); }
            catch { Data = new SettingsData(); }
        }
        else
        {
            Data = new SettingsData
            {
                master = defaultMaster,
                music = defaultMusic,
                sfx = defaultSfx,
                vsync = defaultVSync,
                fpsCap = defaultFpsCap,
                width = defaultWidth,
                height = defaultHeight,
                refresh = defaultRefresh
            };
        }
    }

    private void Save()
    {
        PlayerPrefs.SetString(PREFS_KEY, JsonUtility.ToJson(Data));
        PlayerPrefs.Save();
    }

    private void BuildResolutionsList()
    {
        var all = Screen.resolutions;
        var set = new HashSet<string>();
        _uniqueResolutions = new List<Resolution>();
        foreach (var r in all)
        {
            string key = $"{r.width}x{r.height}@{ToHzInt(r)}"; // dedupe nach gerundetem Hz
            if (set.Add(key)) _uniqueResolutions.Add(r);
        }
        _uniqueResolutions.Sort((a, b) =>
        {
            int areaCmp = (b.width * b.height).CompareTo(a.width * a.height);
            if (areaCmp != 0) return areaCmp;
            return ToHzInt(b).CompareTo(ToHzInt(a));
        });
    }

    static float LinearToDb(float v) => v > 0.0001f ? 20f * Mathf.Log10(v) : -80f;
}
