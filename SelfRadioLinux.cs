using System;
using System.IO;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using NativeUI;
using NAudio.Wave;
using System.Windows.Forms;
using GTA.Math;

public class SelfRadioLinux : Script
{
    private WaveOutEvent _output;
    private AudioFileReader _reader;
    private List<string> _globalTracks = new List<string>();
    private List<string> _filteredTracks = new List<string>();
    private List<string> _currentPlaylist = new List<string>();
    private string _searchFilter = "";
    private int _currentIndex = 0;
    private bool _isPlaying = false;
    private bool _shuffle = false;
    private float _volume = 0.8f;
    private Random _rng = new Random();
    private volatile bool _trackFinished = false;

    private Positional3DSampleProvider _positional;
    private int _audioAnchorVehicle = -1;
    private GTA.Math.Vector3 _audioAnchorPos;
    private bool _positionalAudioEnabled = true;
    private float _refDistance = 3f;
    private float _maxAudibleDistance = 60f;
    private string _distanceHudText = "";

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    private readonly IntPtr _gameWindowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

    private class VehicleState
    {
        public string TrackPath { get; set; }
        public TimeSpan CurrentTime { get; set; }
        public bool WasPlaying { get; set; }
    }
    private Dictionary<int, VehicleState> _vehicleHistory = new Dictionary<int, VehicleState>();
    private int _currentVehicleHandle = -1;
    private int _pruneTimer = 0;

    private bool _autoRadioOff = true;
    private bool _pauseOnFocusOrMenu = true;
    private bool _showProgressBar = true;
    private bool _speedVolumeScaling = false;
    private bool _vehicleOnlyPlayback = true;
    private bool _autoPlayInVehicle = true;
    private bool _autoPaused = false;

    private int _repeatMode = 2;
    private List<string> _repeatModeNames = new List<string> { "Off", "Repeat One", "Repeat All" };

    private HashSet<string> _favoriteTracks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private string _favoritesPath;

    private bool _showVisualizer = true;
    private int _visualizerPosition = 0;
    private List<string> _visualizerPositionNames = new List<string> { "Above HUD", "Below HUD", "Top Center", "Bottom Center", "Left Side", "Right Side" };
    private const int SpectrumBarCount = 14;
    private float[] _spectrumBars = new float[SpectrumBarCount];
    private SpectrumTap _spectrumTap = new SpectrumTap();

    private bool _lowPassFilterEnabled = true;

    private Dictionary<string, string> _trackTitleCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _trackArtistCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private int _themeIndex = 0;
    private int _themeR = 0, _themeG = 150, _themeB = 255;

    private MenuPool _menuPool;
    private UIMenu _menu;
    private UIMenu _settingsMenu;
    private UIMenuCheckboxItem _autoRadioOffCheckbox;
    private UIMenuCheckboxItem _showHudCheckbox;
    private UIMenuCheckboxItem _shuffleCheckbox;
    private UIMenuCheckboxItem _pauseOnFocusCheckbox;
    private UIMenuCheckboxItem _showProgressCheckbox;
    private UIMenuCheckboxItem _speedVolumeCheckbox;
    private UIMenuCheckboxItem _vehicleOnlyCheckbox;
    private UIMenuCheckboxItem _autoPlayCheckbox;
    private UIMenuCheckboxItem _positionalAudioCheckbox;
    private UIMenuCheckboxItem _visualizerCheckbox;
    private UIMenuCheckboxItem _visualizerAlwaysCheckbox;
    private UIMenuCheckboxItem _lowPassCheckbox;
    private UIMenuListItem _themeListItem;
    private UIMenuListItem _repeatListItem;
    private UIMenuListItem _visualizerPosListItem;
    private UIMenuItem _creditsItem;

    private UIMenuItem _searchBtnItem;
    private UIMenuItem _settingsBtnItem;
    private UIMenuItem _favoriteBtnItem;
    private Dictionary<UIMenuItem, string> _trackItemMap = new Dictionary<UIMenuItem, string>();

    private string _nowPlaying = "";
    private int _hudTimer = 0;
    private int _volTimer = 0;

    private string _statusText = "";
    private int _statusTimer = 0;
    private int _statusColorR = 255;
    private int _statusColorG = 255;
    private int _statusColorB = 255;

    private bool _showHud = true;
    private string _musicDir;
    private Keys _keyMenu = Keys.J;
    private Keys _keyPause = Keys.O;
    private Keys _keyNext = Keys.K;
    private Keys _keyPrev = Keys.I;
    private Keys _keyVolUp = Keys.Oemplus;
    private Keys _keyVolDown = Keys.OemMinus;
    private Keys _keyShuffle = Keys.OemSemicolon;
    private Keys _keySeekForward = Keys.OemPeriod;
    private Keys _keySeekBackward = Keys.Oemcomma;
    private Keys _keyRepeat = Keys.R;
    private Keys _keyFavorite = Keys.F;
    private Keys _keyVisualizer = Keys.V;

    private string _scriptsDir;
    private string _iniPath;

    private string _currentDirectory;
    private List<string> _menuFolderPaths = new List<string>();
    private List<string> _menuTrackPaths = new List<string>();
    private bool _isViewingFavorites = false;
    private bool _directoryNeedsLoad = true;
    private bool _globalTracksLoaded = false;

    private string _lastDirectory = "";
    private string _lastTrackPath = "";

    public SelfRadioLinux()
    {
        Function.Call((Hash)0xAA391C728106F7AF, false);
        _scriptsDir = Path.GetDirectoryName(this.Filename);
        if (string.IsNullOrEmpty(_scriptsDir) || !Directory.Exists(_scriptsDir))
            _scriptsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");

        _iniPath = Path.Combine(_scriptsDir, "SelfRadioLinux.ini");
        _favoritesPath = Path.Combine(_scriptsDir, "SelfRadioLinux.favorites");

        string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _musicDir = Path.Combine(docsPath, "Rockstar Games", "GTA V", "User Music");

        LoadConfig();
        LoadFavorites();
        ApplyTheme();

        if (!Directory.Exists(_musicDir))
        {
            string defaultUserMusic = Path.Combine(docsPath, "Rockstar Games", "GTA V", "User Music");
            if (Directory.Exists(defaultUserMusic))
            {
                _musicDir = defaultUserMusic;
                WriteDefaultConfig();
            }
            else
            {
                _musicDir = Path.Combine(_scriptsDir, "Self Radio Music");
            }
        }

        _musicDir = NormalizePath(_musicDir);

        _menuPool = new MenuPool();
        _menu = new UIMenu("Self Radio", "~b~Stopped");
        _menu.Title.Scale = 0.95f;
        _menuPool.Add(_menu);
        _settingsMenu = new UIMenu("Self Radio", "~b~Settings");
        _menuPool.Add(_settingsMenu);

        _autoRadioOffCheckbox = new UIMenuCheckboxItem("Auto-Turn Off Radio", _autoRadioOff, "Automatically turns off standard vehicle radio when entering a vehicle.");
        _showHudCheckbox = new UIMenuCheckboxItem("Show Bottom HUD", _showHud, "Toggle the centered bottom song HUD.");
        _shuffleCheckbox = new UIMenuCheckboxItem("Shuffle Tracks", _shuffle, "Toggle random track playback.");
        _pauseOnFocusCheckbox = new UIMenuCheckboxItem("Pause on Focus Loss/Menu", _pauseOnFocusOrMenu, "Automatically pauses playback when the game loses focus or is in the pause menu.");
        _showProgressCheckbox = new UIMenuCheckboxItem("Show Progress Bar", _showProgressBar, "Displays a smooth timeline/progress bar under the centered HUD.");
        _speedVolumeCheckbox = new UIMenuCheckboxItem("Speed Auto-Volume", _speedVolumeScaling, "Slightly increases music volume as your vehicle goes faster.");
        _vehicleOnlyCheckbox = new UIMenuCheckboxItem("Vehicle-Only Playback", _vehicleOnlyPlayback, "When enabled, music only plays inside vehicles.");
        _autoPlayCheckbox = new UIMenuCheckboxItem("Auto-Play Music in Vehicle", _autoPlayInVehicle, "Automatically starts playing your custom music when you enter a vehicle.");
        _positionalAudioCheckbox = new UIMenuCheckboxItem("3D Exterior Audio", _positionalAudioEnabled, "When you exit a vehicle, audio keeps playing from the car's position.");
        _visualizerCheckbox = new UIMenuCheckboxItem("Show Spectrum Visualizer", _showVisualizer, "Displays animated frequency bars. Use Visualizer Position to move them.");
        _visualizerAlwaysCheckbox = new UIMenuCheckboxItem("Visualizer Always Visible", true, "When enabled, the spectrum bars stay on screen while music plays, even after the song title HUD fades out.");
        _lowPassCheckbox = new UIMenuCheckboxItem("Distance Low-Pass Filter", _lowPassFilterEnabled, "Muffles the exterior car audio the farther you get from the vehicle.");

        var themesList = new List<object> { "Blue Theme", "Green Theme", "Red Theme", "Orange Theme", "Purple Theme" };
        _themeListItem = new UIMenuListItem("HUD Theme", themesList, _themeIndex, "Change the visual color accent of the HUD and bars.");
        var repeatObjects = new List<object>(_repeatModeNames);
        _repeatListItem = new UIMenuListItem("Repeat Mode", repeatObjects, _repeatMode, "Off plays through once, Repeat One loops current, Repeat All loops playlist.");
        var visPosObjects = new List<object>(_visualizerPositionNames);
        _visualizerPosListItem = new UIMenuListItem("Visualizer Position", visPosObjects, _visualizerPosition, "Move the spectrum visualizer bars to different screen locations.");
        _creditsItem = new UIMenuItem("Credits / Author", "Developed by Uzair Mughal. Github: uzairdeveloper223");

        _settingsMenu.AddItem(_autoRadioOffCheckbox);
        _settingsMenu.AddItem(_showHudCheckbox);
        _settingsMenu.AddItem(_shuffleCheckbox);
        _settingsMenu.AddItem(_pauseOnFocusCheckbox);
        _settingsMenu.AddItem(_showProgressCheckbox);
        _settingsMenu.AddItem(_speedVolumeCheckbox);
        _settingsMenu.AddItem(_vehicleOnlyCheckbox);
        _settingsMenu.AddItem(_autoPlayCheckbox);
        _settingsMenu.AddItem(_positionalAudioCheckbox);
        _settingsMenu.AddItem(_visualizerCheckbox);
        _settingsMenu.AddItem(_visualizerAlwaysCheckbox);
        _settingsMenu.AddItem(_visualizerPosListItem);
        _settingsMenu.AddItem(_lowPassCheckbox);
        _settingsMenu.AddItem(_themeListItem);
        _settingsMenu.AddItem(_repeatListItem);
        _settingsMenu.AddItem(_creditsItem);

        _settingsMenu.OnCheckboxChange += (sender, item, checkedState) =>
        {
            if (item == _autoRadioOffCheckbox) { _autoRadioOff = checkedState; ShowStatus(_autoRadioOff ? "AUTO RADIO OFF: ENABLED" : "AUTO RADIO OFF: DISABLED", 3000, _themeR, _themeG, _themeB); }
            else if (item == _showHudCheckbox) { _showHud = checkedState; ShowStatus(_showHud ? "HUD: ENABLED" : "HUD: DISABLED", 3000, _themeR, _themeG, _themeB); }
            else if (item == _shuffleCheckbox) { _shuffle = checkedState; UpdateSubtitle(); ShowStatus(_shuffle ? "SHUFFLE: ON" : "SHUFFLE: OFF", 3000, _shuffle ? 0 : 255, _shuffle ? 255 : 100, 100); }
            else if (item == _pauseOnFocusCheckbox) { _pauseOnFocusOrMenu = checkedState; ShowStatus(_pauseOnFocusOrMenu ? "AUTO-PAUSE: ENABLED" : "AUTO-PAUSE: DISABLED", 3000, _themeR, _themeG, _themeB); }
            else if (item == _showProgressCheckbox) { _showProgressBar = checkedState; ShowStatus(_showProgressBar ? "PROGRESS BAR: ENABLED" : "PROGRESS BAR: DISABLED", 3000, _themeR, _themeG, _themeB); }
            else if (item == _speedVolumeCheckbox) { _speedVolumeScaling = checkedState; ShowStatus(_speedVolumeScaling ? "SPEED AUTO-VOLUME: ON" : "SPEED AUTO-VOLUME: OFF", 3000, _themeR, _themeG, _themeB); }
            else if (item == _vehicleOnlyCheckbox) { _vehicleOnlyPlayback = checkedState; ShowStatus(_vehicleOnlyPlayback ? "VEHICLE PLAYBACK ONLY" : "PLAY EVERYWHERE ENABLED", 3000, _themeR, _themeG, _themeB); if (!_vehicleOnlyPlayback) _currentVehicleHandle = -1; }
            else if (item == _autoPlayCheckbox) { _autoPlayInVehicle = checkedState; ShowStatus(_autoPlayInVehicle ? "AUTO-PLAY IN VEHICLE: ON" : "AUTO-PLAY IN VEHICLE: OFF", 3000, _themeR, _themeG, _themeB); }
            else if (item == _positionalAudioCheckbox) { _positionalAudioEnabled = checkedState; if (!_positionalAudioEnabled && _audioAnchorVehicle != -1) StopPlayback(); ShowStatus(_positionalAudioEnabled ? "3D EXTERIOR AUDIO: ON" : "3D EXTERIOR AUDIO: OFF", 3000, _themeR, _themeG, _themeB); }
            else if (item == _visualizerCheckbox) { _showVisualizer = checkedState; ShowStatus(_showVisualizer ? "VISUALIZER: ENABLED" : "VISUALIZER: DISABLED", 3000, _themeR, _themeG, _themeB); }
            else if (item == _visualizerAlwaysCheckbox) { ShowStatus(checkedState ? "VISUALIZER: ALWAYS ON" : "VISUALIZER: HUD TIMER", 3000, _themeR, _themeG, _themeB); }
            else if (item == _lowPassCheckbox) { _lowPassFilterEnabled = checkedState; if (_positional != null) _positional.LowPassEnabled = _lowPassFilterEnabled; ShowStatus(_lowPassFilterEnabled ? "DISTANCE LOW-PASS: ENABLED" : "DISTANCE LOW-PASS: DISABLED", 3000, _themeR, _themeG, _themeB); }
            WriteDefaultConfig();
        };

        _settingsMenu.OnListChange += (sender, item, index) =>
        {
            if (item == _themeListItem) { _themeIndex = index; ApplyTheme(); ShowStatus($"{themesList[index].ToString().ToUpper()} ACTIVE", 3000, _themeR, _themeG, _themeB); UpdateSubtitle(); WriteDefaultConfig(); }
            else if (item == _repeatListItem) { _repeatMode = index; ShowStatus($"REPEAT MODE: {_repeatModeNames[index].ToUpper()}", 3000, _themeR, _themeG, _themeB); WriteDefaultConfig(); }
            else if (item == _visualizerPosListItem) { _visualizerPosition = index; ShowStatus($"VISUALIZER: {_visualizerPositionNames[index].ToUpper()}", 3000, _themeR, _themeG, _themeB); WriteDefaultConfig(); }
        };

        _settingsMenu.OnItemSelect += (sender, item, index) =>
        {
            if (item == _creditsItem) ShowStatus("DEVELOPED BY UZAIR MUGHAL", 4000, _themeR, _themeG, _themeB);
        };

        _menu.OnItemSelect += (sender, item, index) =>
        {
            if (item == _searchBtnItem) { TriggerSearch(); return; }
            if (item == _favoriteBtnItem) { ToggleFavoriteCurrent(); return; }
            if (item == _settingsBtnItem) { return; }

            bool isSearching = !string.IsNullOrEmpty(_searchFilter);
            if (isSearching)
            {
                if (_trackItemMap.TryGetValue(item, out string path)) PlayTrackByPath(path);
                return;
            }

            if (_isViewingFavorites)
            {
                if (index == 1) { _isViewingFavorites = false; _directoryNeedsLoad = true; RefreshMenu(); return; }
                if (_trackItemMap.TryGetValue(item, out string path)) PlayTrackByPath(path);
                return;
            }

            bool isAtRoot = NormalizePath(_currentDirectory) == NormalizePath(_musicDir);

            if (isAtRoot)
            {
                if (index == 1)
                {
                    _isViewingFavorites = true;
                    _currentPlaylist = new List<string>();
                    foreach (var fav in _favoriteTracks)
                    {
                        if (File.Exists(fav)) _currentPlaylist.Add(fav);
                    }
                    RefreshMenu();
                    return;
                }
                int folderIndex = index - 2;
                if (folderIndex >= 0 && folderIndex < _menuFolderPaths.Count) { EnterDirectory(_menuFolderPaths[folderIndex]); return; }
                if (_trackItemMap.TryGetValue(item, out string path)) PlayTrackByPath(path);
            }
            else
            {
                if (index == 1) { GoBackDirectory(); return; }
                int folderIndex = index - 2;
                if (folderIndex >= 0 && folderIndex < _menuFolderPaths.Count) { EnterDirectory(_menuFolderPaths[folderIndex]); return; }
                if (_trackItemMap.TryGetValue(item, out string path)) PlayTrackByPath(path);
            }
        };

        Tick += OnTick;
        KeyUp += OnKeyUp;
        Aborted += OnAborted;

        if (!string.IsNullOrEmpty(_lastDirectory))
        {
            string normalizedLast = NormalizePath(_lastDirectory);
            if (Directory.Exists(normalizedLast))
            {
                _currentDirectory = normalizedLast;
            }
            else
            {
                _currentDirectory = _musicDir;
            }
        }
        else
        {
            _currentDirectory = _musicDir;
        }

        _directoryNeedsLoad = true;

        if (!string.IsNullOrEmpty(_lastTrackPath))
        {
            string normalizedTrack = NormalizePath(_lastTrackPath);
            if (File.Exists(normalizedTrack))
            {
                _nowPlaying = GetTrackDisplayName(normalizedTrack);
            }
        }
    }

    private string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        string result = path.Replace('\\', '/');
        while (result.Contains("//")) result = result.Replace("//", "/");
        return result.TrimEnd('/');
    }

    private bool IsAtMusicRoot()
    {
        return NormalizePath(_currentDirectory) == NormalizePath(_musicDir);
    }

    private string GetParentDirectory(string path)
    {
        string normalized = NormalizePath(path);
        string root = NormalizePath(_musicDir);

        if (normalized == root) return null;

        int lastSlash = normalized.LastIndexOf('/');
        if (lastSlash <= 0) return null;

        string parent = normalized.Substring(0, lastSlash);

        if (parent.Length < root.Length) return root;

        return parent;
    }

    private void LoadCurrentDirectory()
    {
        _currentPlaylist.Clear();
        _menuFolderPaths.Clear();
        _menuTrackPaths.Clear();
        _isViewingFavorites = false;

        _currentDirectory = NormalizePath(_currentDirectory);

        if (!Directory.Exists(_currentDirectory))
        {
            _currentDirectory = _musicDir;
        }

        try
        {
            var dirs = Directory.GetDirectories(_currentDirectory);
            var sortedDirs = new List<string>();
            foreach (var d in dirs) sortedDirs.Add(NormalizePath(d));
            sortedDirs.Sort(StringComparer.OrdinalIgnoreCase);
            _menuFolderPaths.AddRange(sortedDirs);
        }
        catch { }

        try
        {
            var files = Directory.GetFiles(_currentDirectory);
            var validFiles = new List<string>();
            foreach (var f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".mp3" || ext == ".wav") validFiles.Add(NormalizePath(f));
            }
            validFiles.Sort(StringComparer.OrdinalIgnoreCase);
            _currentPlaylist = validFiles;
            _menuTrackPaths.AddRange(validFiles);
        }
        catch { }

        _directoryNeedsLoad = false;
        RefreshMenu();
    }

    private void EnterDirectory(string path)
    {
        _currentDirectory = NormalizePath(path);
        LoadCurrentDirectory();
        _menu.Visible = true;
    }

    private void GoBackDirectory()
    {
        string parent = GetParentDirectory(_currentDirectory);
        if (!string.IsNullOrEmpty(parent))
        {
            _currentDirectory = parent;
            LoadCurrentDirectory();
            _menu.Visible = true;
        }
    }

    private void SafeRecursiveScan(string dir, List<string> results)
    {
        try
        {
            var files = Directory.GetFiles(dir);
            foreach (var f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".mp3" || ext == ".wav") results.Add(NormalizePath(f));
            }
        }
        catch { }

        try
        {
            var dirs = Directory.GetDirectories(dir);
            foreach (var d in dirs) SafeRecursiveScan(d, results);
        }
        catch { }
    }

    private void EnsureGlobalTracksLoaded()
    {
        if (_globalTracksLoaded) return;
        _globalTracks.Clear();
        SafeRecursiveScan(_musicDir, _globalTracks);
        _globalTracks.Sort(StringComparer.OrdinalIgnoreCase);
        _globalTracksLoaded = true;
    }

    private void TriggerSearch()
    {
        if (!string.IsNullOrEmpty(_searchFilter))
        {
            _searchFilter = "";
            _filteredTracks.Clear();
            _directoryNeedsLoad = true;
            RefreshMenu();
            ShowStatus("SEARCH FILTER CLEARED", 2500, _themeR, _themeG, _themeB);
            return;
        }
        string query = Game.GetUserInput(40);
        if (!string.IsNullOrEmpty(query))
        {
            _searchFilter = query.Trim();
            EnsureGlobalTracksLoaded();
            _filteredTracks.Clear();
            foreach (var f in _globalTracks)
            {
                string name = GetTrackDisplayName(f);
                if (name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    _filteredTracks.Add(f);
            }
            RefreshMenu();
            ShowStatus($"FILTERED: {_filteredTracks.Count} SONGS", 3000, _themeR, _themeG, _themeB);
        }
    }

    private void RefreshMenu()
    {
        _menu.Clear();
        _trackItemMap.Clear();

        bool isSearching = !string.IsNullOrEmpty(_searchFilter);

        if (isSearching)
        {
            string searchLabel = $"Search: \"{_searchFilter}\" ~r~[Clear]";
            _searchBtnItem = new UIMenuItem(searchLabel, "Click to clear active filter.");
            _menu.AddItem(_searchBtnItem);

            foreach (var f in _filteredTracks)
            {
                string displayName = GetTrackDisplayName(f);
                if (_favoriteTracks.Contains(f)) displayName = "~y~*~s~ " + displayName;
                bool isCurrent = _isPlaying && _currentPlaylist.Contains(f) && _currentPlaylist.IndexOf(f) == _currentIndex;
                string prefix = isCurrent ? "~g~> ~w~" : "";
                var trackItem = new UIMenuItem($"{prefix}{displayName}");
                _menu.AddItem(trackItem);
                _trackItemMap[trackItem] = f;
            }
        }
        else if (_isViewingFavorites)
        {
            _searchBtnItem = new UIMenuItem("Search Tracks...", "Click to search songs.");
            _menu.AddItem(_searchBtnItem);

            var backBtn = new UIMenuItem("~y~.. (Back to Library)", "Return to folder browser.");
            _menu.AddItem(backBtn);

            foreach (var f in _currentPlaylist)
            {
                string displayName = GetTrackDisplayName(f);
                bool isCurrent = _isPlaying && _currentPlaylist.Contains(f) && _currentPlaylist.IndexOf(f) == _currentIndex;
                string prefix = isCurrent ? "~g~> ~w~" : "";
                var trackItem = new UIMenuItem($"{prefix}~y~*~s~ {displayName}");
                _menu.AddItem(trackItem);
                _trackItemMap[trackItem] = f;
            }
        }
        else
        {
            _searchBtnItem = new UIMenuItem("Search Tracks...", "Click to search songs.");
            _menu.AddItem(_searchBtnItem);

            bool isAtRoot = IsAtMusicRoot();

            if (isAtRoot)
            {
                var favFolderItem = new UIMenuItem("~y~[Favorites] ~w~Favorite Tracks", "View all favorited tracks.");
                _menu.AddItem(favFolderItem);
            }
            else
            {
                var backBtn = new UIMenuItem("~y~.. (Back)", "Go up one directory.");
                _menu.AddItem(backBtn);
            }

            foreach (var dir in _menuFolderPaths)
            {
                string dirName = Path.GetFileName(dir);
                var dirItem = new UIMenuItem($"~b~[Folder] ~w~{SanitizeForGta(dirName)}", "Open folder");
                _menu.AddItem(dirItem);
            }

            foreach (var f in _menuTrackPaths)
            {
                string displayName = GetTrackDisplayName(f);
                if (_favoriteTracks.Contains(f)) displayName = "~y~*~s~ " + displayName;
                bool isCurrent = _isPlaying && _currentPlaylist.Contains(f) && _currentPlaylist.IndexOf(f) == _currentIndex;
                string prefix = isCurrent ? "~g~> ~w~" : "";
                var trackItem = new UIMenuItem($"{prefix}{displayName}");
                _menu.AddItem(trackItem);
                _trackItemMap[trackItem] = f;
            }
        }

        _favoriteBtnItem = new UIMenuItem(IsCurrentFavorite() ? "~r~Unfavorite Current Track" : "~y~Favorite Current Track", "Mark or unmark the currently loaded track as a favorite.");
        _menu.AddItem(_favoriteBtnItem);

        _settingsBtnItem = new UIMenuItem("Settings Menu", "Configure script options and preferences.");
        _menu.AddItem(_settingsBtnItem);
        _menu.BindMenuToItem(_settingsMenu, _settingsBtnItem);
        UpdateSubtitle();
    }

    private void PlayTrackByPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        if (!_currentPlaylist.Contains(path))
        {
            if (!string.IsNullOrEmpty(_searchFilter))
                _currentPlaylist = new List<string>(_filteredTracks);
            else
                _currentPlaylist.Add(path);
        }

        int idx = _currentPlaylist.IndexOf(path);
        if (idx >= 0) PlayTrack(idx);
    }

    private void PlayTrack(int index)
    {
        if (_currentPlaylist.Count == 0) return;
        int preservedAnchor = _audioAnchorVehicle;
        StopPlayback();
        _currentIndex = index;
        string path = _currentPlaylist[index];
        try
        {
            _output = new WaveOutEvent();
            _reader = new AudioFileReader(path) { Volume = _volume };
            _positional = new Positional3DSampleProvider(_reader, _spectrumTap);
            _positional.LowPassEnabled = _lowPassFilterEnabled;
            var pcmProvider = new Float32ToPcm16Provider(_positional);
            _output.Init(pcmProvider);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Play();
            _isPlaying = true;
            _nowPlaying = GetTrackDisplayName(path);
            _hudTimer = Game.GameTime + 5000;
            UpdateSubtitle();
            _audioAnchorVehicle = preservedAnchor;
            if (_audioAnchorVehicle != -1) UpdatePositionalAudio();
        }
        catch (Exception ex) { UI.Notify("~r~SelfRadio Error: " + ex.Message); }
    }

    private void StopPlayback()
    {
        _isPlaying = false;
        if (_output != null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            try { _output.Stop(); } catch { }
            try { _output.Dispose(); } catch { }
            _output = null;
        }
        if (_reader != null)
        {
            try { _reader.Dispose(); } catch { }
            _reader = null;
        }
        _positional = null;
        _audioAnchorVehicle = -1;
    }

    private void TogglePause()
    {
        if (_output == null && _currentPlaylist.Count > 0) { PlayTrack(_currentIndex); return; }
        if (_output?.PlaybackState == PlaybackState.Playing) { _output.Pause(); _hudTimer = Game.GameTime + 5000; }
        else if (_output?.PlaybackState == PlaybackState.Paused) { _output.Play(); _hudTimer = Game.GameTime + 5000; }
        UpdateSubtitle();
    }

    private void SetVolume(float delta)
    {
        _volume = Math.Max(0f, Math.Min(1f, _volume + delta));
        if (_reader != null) _reader.Volume = _volume;
        _volTimer = Game.GameTime + 3000;
    }

    private void UpdatePositionalAudio()
    {
        if (_positional == null) return;
        if (_audioAnchorVehicle == -1) { _positional.Pan = 0f; _positional.Gain = 1f; _distanceHudText = ""; return; }

        GTA.Math.Vector3 sourcePos;
        var anchorVeh = new Vehicle(_audioAnchorVehicle);
        if (anchorVeh.Exists()) { sourcePos = anchorVeh.Position; _audioAnchorPos = sourcePos; }
        else { sourcePos = _audioAnchorPos; }

        GTA.Math.Vector3 listenerPos = GameplayCamera.Position;
        GTA.Math.Vector3 forward = GameplayCamera.Direction;
        forward.Z = 0f;
        if (forward.LengthSquared() < 0.0001f) forward = new GTA.Math.Vector3(0f, 1f, 0f);
        forward.Normalize();
        GTA.Math.Vector3 right = new GTA.Math.Vector3(forward.Y, -forward.X, 0f);

        GTA.Math.Vector3 toSource = sourcePos - listenerPos;
        float distance = toSource.Length();
        float pan = 0f;
        if (distance > 0.05f)
        {
            GTA.Math.Vector3 dir = toSource / distance;
            pan = GTA.Math.Vector3.Dot(dir, right);
            pan = Math.Max(-1f, Math.Min(1f, pan));
        }

        float gain;
        if (distance <= _refDistance) gain = 1f;
        else if (distance >= _maxAudibleDistance) gain = 0f;
        else { float t = (distance - _refDistance) / (_maxAudibleDistance - _refDistance); gain = (1f - t) * (1f - t); }

        _positional.Pan = pan;
        _positional.Gain = gain;
        _distanceHudText = gain > 0f ? $"{(int)distance}m FROM THE CAR" : "OUT OF RANGE";
    }

    private void ShowStatus(string text, int durationMs = 3000, int r = 255, int g = 255, int b = 255)
    {
        _statusText = text;
        _statusTimer = Game.GameTime + durationMs;
        _statusColorR = r;
        _statusColorG = g;
        _statusColorB = b;
    }

    private void PruneVehicleHistory()
    {
        List<int> toRemove = new List<int>();
        foreach (int handle in _vehicleHistory.Keys) { if (!new Vehicle(handle).Exists()) toRemove.Add(handle); }
        foreach (int handle in toRemove) _vehicleHistory.Remove(handle);
    }

    private void SaveVehicleState(int handle)
    {
        VehicleState state = new VehicleState
        {
            TrackPath = (_currentPlaylist.Count > 0 && _currentIndex >= 0 && _currentIndex < _currentPlaylist.Count) ? _currentPlaylist[_currentIndex] : "",
            CurrentTime = _reader != null ? _reader.CurrentTime : TimeSpan.Zero,
            WasPlaying = _isPlaying
        };
        _vehicleHistory[handle] = state;
    }

    private void LoadFavorites()
    {
        _favoriteTracks.Clear();
        try
        {
            if (File.Exists(_favoritesPath))
            {
                foreach (var line in File.ReadAllLines(_favoritesPath))
                {
                    string trimmed = NormalizePath(line.Trim());
                    if (!string.IsNullOrEmpty(trimmed)) _favoriteTracks.Add(trimmed);
                }
            }
        }
        catch { }
    }

    private void SaveFavorites()
    {
        try { File.WriteAllLines(_favoritesPath, _favoriteTracks); } catch { }
    }

    private bool IsCurrentFavorite()
    {
        if (_currentPlaylist.Count == 0 || _currentIndex < 0 || _currentIndex >= _currentPlaylist.Count) return false;
        return _favoriteTracks.Contains(_currentPlaylist[_currentIndex]);
    }

    private void ToggleFavoriteCurrent()
    {
        if (_currentPlaylist.Count == 0 || _currentIndex < 0 || _currentIndex >= _currentPlaylist.Count) return;
        string path = _currentPlaylist[_currentIndex];
        if (_favoriteTracks.Contains(path))
        {
            _favoriteTracks.Remove(path);
            ShowStatus("REMOVED FROM FAVORITES", 2500, _themeR, _themeG, _themeB);
        }
        else
        {
            _favoriteTracks.Add(path);
            ShowStatus("ADDED TO FAVORITES", 2500, 255, 215, 0);
        }
        SaveFavorites();
        RefreshMenu();
    }

    private void EnsureTagsCached(string path)
    {
        if (_trackTitleCache.ContainsKey(path)) return;
        string title = null;
        string artist = null;
        try { Id3TagReader.ReadTags(path, out title, out artist); } catch { }
        _trackTitleCache[path] = title;
        _trackArtistCache[path] = artist;
    }

    private string GetTrackArtist(string path) { EnsureTagsCached(path); string a; return _trackArtistCache.TryGetValue(path, out a) ? a : null; }
    private string GetTrackTitle(string path) { EnsureTagsCached(path); string t; return _trackTitleCache.TryGetValue(path, out t) ? t : null; }

    private string GetTrackDisplayName(string path)
    {
        string title = GetTrackTitle(path);
        string artist = GetTrackArtist(path);
        string display;
        if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(artist)) display = $"{artist} - {title}";
        else if (!string.IsNullOrEmpty(title)) display = title;
        else display = Path.GetFileNameWithoutExtension(path);
        display = SanitizeForGta(display);
        return string.IsNullOrEmpty(display) ? "Unknown Track" : display;
    }

    private string SanitizeForGta(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        string sanitized = input.Replace("\u266a", "").Replace("\u266b", "").Replace("\uff5c", "|").Replace("\uff1a", ":").Trim();
        var sb = new System.Text.StringBuilder();
        foreach (char c in sanitized) if (!char.IsControl(c) && !char.IsSurrogate(c)) sb.Append(c);
        return sb.ToString().Trim();
    }

    private void UpdateSubtitle()
    {
        string colorCode = GetThemeColorCode();
        _menu.Title.Caption = _shuffle ? $"Self Radio {colorCode}[SHUFFLE]" : "Self Radio";
        bool currentlyPlaying = _output?.PlaybackState == PlaybackState.Playing;
        string currentDirName = _isViewingFavorites ? "Favorites" : (IsAtMusicRoot() ? "Root" : Path.GetFileName(_currentDirectory));
        _menu.Subtitle.Caption = _currentPlaylist.Count == 0 && _menuFolderPaths.Count == 0
            ? "~r~Empty folder"
            : currentlyPlaying
                ? $"{colorCode}PLAYING: ~w~{_nowPlaying}"
                : $"~c~{SanitizeForGta(currentDirName)}";
    }

    private void ApplyTheme()
    {
        switch (_themeIndex)
        {
            case 0: _themeR = 0; _themeG = 150; _themeB = 255; break;
            case 1: _themeR = 46; _themeG = 204; _themeB = 113; break;
            case 2: _themeR = 231; _themeG = 76; _themeB = 60; break;
            case 3: _themeR = 230; _themeG = 126; _themeB = 34; break;
            case 4: _themeR = 155; _themeG = 89; _themeB = 182; break;
            default: _themeIndex = 0; _themeR = 0; _themeG = 150; _themeB = 255; break;
        }
    }

    private string GetThemeColorCode()
    {
        switch (_themeIndex) { case 0: return "~b~"; case 1: return "~g~"; case 2: return "~r~"; case 3: return "~o~"; case 4: return "~p~"; default: return "~b~"; }
    }

    private void LoadConfig()
    {
        if (!File.Exists(_iniPath)) { WriteDefaultConfig(); return; }
        foreach (var raw in File.ReadAllLines(_iniPath))
        {
            var line = raw.Trim();
            if (line.StartsWith(";") || line.StartsWith("[") || !line.Contains("=")) continue;
            var parts = line.Split(new[] { '=' }, 2);
            var k = parts[0].Trim();
            var v = parts[1].Trim();
            switch (k)
            {
                case "MusicDir": _musicDir = NormalizePath(v); break;
                case "Volume": float.TryParse(v, out _volume); break;
                case "Shuffle": _shuffle = v == "1"; break;
                case "ShowHud": _showHud = v == "1"; break;
                case "AutoRadioOff": _autoRadioOff = v == "1"; break;
                case "PauseOnFocus": _pauseOnFocusOrMenu = v == "1"; break;
                case "ShowProgress": _showProgressBar = v == "1"; break;
                case "SpeedVolume": _speedVolumeScaling = v == "1"; break;
                case "VehicleOnly": _vehicleOnlyPlayback = v == "1"; break;
                case "AutoPlayInVehicle": _autoPlayInVehicle = v == "1"; break;
                case "PositionalAudio": _positionalAudioEnabled = v == "1"; break;
                case "MaxAudibleDistance": float.TryParse(v, out _maxAudibleDistance); break;
                case "ThemeIndex": int.TryParse(v, out _themeIndex); break;
                case "CurrentIndex": int.TryParse(v, out _currentIndex); break;
                case "RepeatMode": int.TryParse(v, out _repeatMode); break;
                case "ShowVisualizer": _showVisualizer = v == "1"; break;
                case "VisualizerPosition": int.TryParse(v, out _visualizerPosition); break;
                case "LowPassFilter": _lowPassFilterEnabled = v == "1"; break;
                case "LastDirectory": _lastDirectory = NormalizePath(v); break;
                case "LastTrackPath": _lastTrackPath = NormalizePath(v); break;
                case "KeyMenu": Enum.TryParse<Keys>(v, out _keyMenu); break;
                case "KeyPause": Enum.TryParse<Keys>(v, out _keyPause); break;
                case "KeyNext": Enum.TryParse<Keys>(v, out _keyNext); break;
                case "KeyPrev": Enum.TryParse<Keys>(v, out _keyPrev); break;
                case "KeyVolUp": Enum.TryParse<Keys>(v, out _keyVolUp); break;
                case "KeyVolDown": Enum.TryParse<Keys>(v, out _keyVolDown); break;
                case "KeyShuffle": Enum.TryParse<Keys>(v, out _keyShuffle); break;
                case "KeySeekForward": Enum.TryParse<Keys>(v, out _keySeekForward); break;
                case "KeySeekBackward": Enum.TryParse<Keys>(v, out _keySeekBackward); break;
                case "KeyRepeat": Enum.TryParse<Keys>(v, out _keyRepeat); break;
                case "KeyFavorite": Enum.TryParse<Keys>(v, out _keyFavorite); break;
                case "KeyVisualizer": Enum.TryParse<Keys>(v, out _keyVisualizer); break;
            }
        }
    }

    private void WriteDefaultConfig()
    {
        try
        {
            string currentTrackPath = "";
            if (_currentPlaylist.Count > 0 && _currentIndex >= 0 && _currentIndex < _currentPlaylist.Count)
                currentTrackPath = NormalizePath(_currentPlaylist[_currentIndex]);

            File.WriteAllText(_iniPath,
                "[Settings]\n" +
                $"MusicDir={NormalizePath(_musicDir)}\n" +
                $"Volume={_volume}\n" +
                $"Shuffle={(_shuffle ? "1" : "0")}\n" +
                $"ShowHud={(_showHud ? "1" : "0")}\n" +
                $"AutoRadioOff={(_autoRadioOff ? "1" : "0")}\n" +
                $"PauseOnFocus={(_pauseOnFocusOrMenu ? "1" : "0")}\n" +
                $"ShowProgress={(_showProgressBar ? "1" : "0")}\n" +
                $"SpeedVolume={(_speedVolumeScaling ? "1" : "0")}\n" +
                $"VehicleOnly={(_vehicleOnlyPlayback ? "1" : "0")}\n" +
                $"AutoPlayInVehicle={(_autoPlayInVehicle ? "1" : "0")}\n" +
                $"PositionalAudio={(_positionalAudioEnabled ? "1" : "0")}\n" +
                $"MaxAudibleDistance={_maxAudibleDistance}\n" +
                $"ThemeIndex={_themeIndex}\n" +
                $"CurrentIndex={_currentIndex}\n" +
                $"RepeatMode={_repeatMode}\n" +
                $"ShowVisualizer={(_showVisualizer ? "1" : "0")}\n" +
                $"VisualizerPosition={_visualizerPosition}\n" +
                $"LowPassFilter={(_lowPassFilterEnabled ? "1" : "0")}\n" +
                $"LastDirectory={NormalizePath(_currentDirectory)}\n" +
                $"LastTrackPath={currentTrackPath}\n" +
                "[Keys]\n" +
                $"KeyMenu={_keyMenu}\n" +
                $"KeyPause={_keyPause}\n" +
                $"KeyNext={_keyNext}\n" +
                $"KeyPrev={_keyPrev}\n" +
                $"KeyVolUp={_keyVolUp}\n" +
                $"KeyVolDown={_keyVolDown}\n" +
                $"KeyShuffle={_keyShuffle}\n" +
                $"KeySeekForward={_keySeekForward}\n" +
                $"KeySeekBackward={_keySeekBackward}\n" +
                $"KeyRepeat={_keyRepeat}\n" +
                $"KeyFavorite={_keyFavorite}\n" +
                $"KeyVisualizer={_keyVisualizer}\n"
            );
        }
        catch (Exception ex) { UI.Notify("~r~SelfRadio Config Error: " + ex.Message); }
    }

    private void DrawHud()
    {
        int currentTime = Game.GameTime;
        if (currentTime < _volTimer)
        {
            int volRemaining = _volTimer - currentTime;
            int volAlpha = 220, textAlpha = 200, bgAlpha = 150;
            if (volRemaining < 1000) { float mult = volRemaining / 1000f; volAlpha = (int)(volAlpha * mult); textAlpha = (int)(textAlpha * mult); bgAlpha = (int)(bgAlpha * mult); }
            float bgX = 0.5f, bgY = 0.93f, bgW = 0.15f, bgH = 0.008f;
            Function.Call(Hash.DRAW_RECT, bgX, bgY, bgW, bgH, 0, 0, 0, bgAlpha);
            float fgW = bgW * _volume, fgX = bgX - (bgW / 2f) + (fgW / 2f);
            Function.Call(Hash.DRAW_RECT, fgX, bgY, fgW, bgH, _themeR, _themeG, _themeB, volAlpha);
            Function.Call(Hash.SET_TEXT_FONT, 0); Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.25f); Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, textAlpha);
            Function.Call(Hash.SET_TEXT_CENTRE, true); Function.Call(Hash._SET_TEXT_ENTRY, "STRING"); Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, $"VOLUME: {(int)(_volume * 100)}%"); Function.Call(Hash._DRAW_TEXT, bgX, bgY - 0.022f);
        }
        if (currentTime < _statusTimer)
        {
            int shRemaining = _statusTimer - currentTime; int shAlpha = 200;
            if (shRemaining < 1000) shAlpha = (int)(200 * (shRemaining / 1000f));
            Function.Call(Hash.SET_TEXT_FONT, 0); Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.47f); Function.Call(Hash.SET_TEXT_COLOUR, _statusColorR, _statusColorG, _statusColorB, shAlpha);
            Function.Call(Hash.SET_TEXT_CENTRE, true); Function.Call(Hash._SET_TEXT_ENTRY, "STRING"); Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, _statusText); Function.Call(Hash._DRAW_TEXT, 0.5f, 0.908f);
        }
        if (_showHud && _isPlaying && currentTime < _hudTimer)
        {
            int timeRemaining = _hudTimer - currentTime; int alpha = 255;
            if (timeRemaining < 1000) { alpha = (int)((timeRemaining / 1000f) * 255f); if (alpha < 0) alpha = 0; }
            float textX = 0.5f, textY = 0.81f;
            bool isCurrentlyPlaying = _output?.PlaybackState == PlaybackState.Playing;
            string status = isCurrentlyPlaying ? "NOW PLAYING" : "PAUSED";
            Function.Call(Hash.SET_TEXT_FONT, 4); Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.38f); Function.Call(Hash.SET_TEXT_COLOUR, _themeR, _themeG, _themeB, alpha);
            Function.Call(Hash.SET_TEXT_CENTRE, true); Function.Call(Hash._SET_TEXT_ENTRY, "STRING"); Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, status); Function.Call(Hash._DRAW_TEXT, textX, textY);
            Function.Call(Hash.SET_TEXT_FONT, 4); Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.62f); Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, alpha);
            Function.Call(Hash.SET_TEXT_CENTRE, true); Function.Call(Hash.SET_TEXT_DROPSHADOW, 2, 0, 0, 0, alpha); Function.Call(Hash.SET_TEXT_EDGE, 1, 0, 0, 0, (int)(alpha * 0.8f));
            Function.Call(Hash._SET_TEXT_ENTRY, "STRING"); Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, _nowPlaying); Function.Call(Hash._DRAW_TEXT, textX, textY + 0.038f);
            if (_showProgressBar && _reader != null)
            {
                TimeSpan current = _reader.CurrentTime, total = _reader.TotalTime;
                double pct = total.TotalSeconds > 0 ? current.TotalSeconds / total.TotalSeconds : 0;
                float bgX = 0.5f, bgY = textY + 0.088f, bgW = 0.15f, bgH = 0.003f;
                Function.Call(Hash.DRAW_RECT, bgX, bgY, bgW, bgH, 150, 150, 150, (int)(alpha * 0.3f));
                float fgW = bgW * (float)pct, fgX = bgX - (bgW / 2f) + (fgW / 2f);
                Function.Call(Hash.DRAW_RECT, fgX, bgY, fgW, bgH, _themeR, _themeG, _themeB, alpha);
                string timeStr = $"{current.Minutes:D2}:{current.Seconds:D2} / {total.Minutes:D2}:{total.Seconds:D2}";
                Function.Call(Hash.SET_TEXT_FONT, 0); Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.20f); Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, alpha);
                Function.Call(Hash.SET_TEXT_CENTRE, true); Function.Call(Hash._SET_TEXT_ENTRY, "STRING"); Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, timeStr); Function.Call(Hash._DRAW_TEXT, bgX, bgY + 0.004f);
            }
        }
    }

    private void UpdateSpectrumVisualizer()
    {
        if (!_showVisualizer || !_isPlaying)
        {
            for (int i = 0; i < _spectrumBars.Length; i++) _spectrumBars[i] *= 0.85f;
            return;
        }
        float[] samples = _spectrumTap.SnapshotLatest(SpectrumAnalyzer.FftSize);
        float[] mags = new float[SpectrumAnalyzer.FftSize / 2];
        SpectrumAnalyzer.ComputeMagnitudes(samples, mags);
        int usableBins = mags.Length - 1;
        for (int bar = 0; bar < _spectrumBars.Length; bar++)
        {
            double t0 = Math.Pow((double)bar / _spectrumBars.Length, 2.0);
            double t1 = Math.Pow((double)(bar + 1) / _spectrumBars.Length, 2.0);
            int binStart = 1 + (int)(t0 * usableBins);
            int binEnd = Math.Max(binStart + 1, 1 + (int)(t1 * usableBins));
            binEnd = Math.Min(binEnd, mags.Length);
            float sum = 0f; int cnt = 0;
            for (int b = binStart; b < binEnd; b++) { sum += mags[b]; cnt++; }
            float avg = cnt > 0 ? sum / cnt : 0f;
            float normalized = Math.Min(1f, avg * 0.12f);
            float target = (float)Math.Sqrt(normalized);
            float rate = target > _spectrumBars[bar] ? 0.6f : 0.2f;
            _spectrumBars[bar] += (target - _spectrumBars[bar]) * rate;
        }
    }

    private void DrawSpectrumVisualizer()
    {
        if (!_showVisualizer || !_isPlaying) return;

        int barCount = _spectrumBars.Length;
        float totalWidth = 0.16f, barGap = 0.0016f;
        float barWidth = (totalWidth - barGap * (barCount - 1)) / barCount;
        float maxHeight = 0.024f;
        float baseX = 0.5f, baseY = 0.792f;
        bool vertical = false;

        switch (_visualizerPosition)
        {
            case 0: baseX = 0.5f; baseY = 0.792f; vertical = false; break;
            case 1: baseX = 0.5f; baseY = 0.92f; vertical = false; break;
            case 2: baseX = 0.5f; baseY = 0.08f; vertical = false; break;
            case 3: baseX = 0.5f; baseY = 0.95f; vertical = false; break;
            case 4: baseX = 0.06f; baseY = 0.5f; vertical = true; break;
            case 5: baseX = 0.94f; baseY = 0.5f; vertical = true; break;
            default: baseX = 0.5f; baseY = 0.792f; vertical = false; break;
        }

        if (!vertical)
        {
            float startX = baseX - totalWidth / 2f + barWidth / 2f;
            for (int i = 0; i < barCount; i++)
            {
                float h = Math.Max(0.0015f, _spectrumBars[i] * maxHeight);
                float x = startX + i * (barWidth + barGap);
                float y = baseY - h / 2f;
                Function.Call(Hash.DRAW_RECT, x, y, barWidth, h, _themeR, _themeG, _themeB, 200);
            }
        }
        else
        {
            float totalHeight = 0.25f;
            float vBarGap = 0.002f;
            float vBarHeight = (totalHeight - vBarGap * (barCount - 1)) / barCount;
            float vBarMaxWidth = 0.03f;
            float startY = baseY - totalHeight / 2f + vBarHeight / 2f;
            for (int i = 0; i < barCount; i++)
            {
                float w = Math.Max(0.002f, _spectrumBars[i] * vBarMaxWidth);
                float y = startY + i * (vBarHeight + vBarGap);
                float x = baseX + w / 2f;
                Function.Call(Hash.DRAW_RECT, x, y, w, vBarHeight, _themeR, _themeG, _themeB, 200);
            }
        }
    }

    private void DrawExteriorAudioHud()
    {
        if (_audioAnchorVehicle == -1 || !_isPlaying || string.IsNullOrEmpty(_distanceHudText)) return;
        Function.Call(Hash.SET_TEXT_FONT, 0); Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.30f); Function.Call(Hash.SET_TEXT_COLOUR, _themeR, _themeG, _themeB, 210);
        Function.Call(Hash.SET_TEXT_CENTRE, true); Function.Call(Hash._SET_TEXT_ENTRY, "STRING"); Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, _distanceHudText); Function.Call(Hash._DRAW_TEXT, 0.5f, 0.955f);
    }

    private void OnPlaybackStopped(object sender, StoppedEventArgs e) { _trackFinished = true; }

    private void OnTick(object sender, EventArgs e)
    {
        _menuPool.ProcessMenus();

        if (_directoryNeedsLoad && _menu.Visible)
        {
            LoadCurrentDirectory();
        }

        UpdateSpectrumVisualizer();
        DrawHud();
        DrawSpectrumVisualizer();
        DrawExteriorAudioHud();

        if (Game.GameTime > _pruneTimer)
        {
            _pruneTimer = Game.GameTime + 30000;
            PruneVehicleHistory();
        }

        if (_trackFinished)
        {
            _trackFinished = false;
            if (_currentPlaylist.Count > 0)
            {
                if (_repeatMode == 1)
                {
                    PlayTrack(_currentIndex);
                }
                else if (_repeatMode == 2)
                {
                    int next = _shuffle ? _rng.Next(_currentPlaylist.Count) : (_currentIndex + 1) % _currentPlaylist.Count;
                    PlayTrack(next);
                }
                else
                {
                    bool isLast = !_shuffle && _currentIndex >= _currentPlaylist.Count - 1;
                    if (isLast) { StopPlayback(); UpdateSubtitle(); }
                    else
                    {
                        int next = _shuffle ? _rng.Next(_currentPlaylist.Count) : (_currentIndex + 1) % _currentPlaylist.Count;
                        PlayTrack(next);
                    }
                }
            }
        }

        Ped playerPed = Game.Player.Character;
        bool inVehicle = playerPed != null && playerPed.IsInVehicle();
        Vehicle curVeh = inVehicle ? playerPed.CurrentVehicle : null;
        int vehHandle = curVeh != null ? curVeh.Handle : -1;

        if (_vehicleOnlyPlayback)
        {
            if (inVehicle)
            {
                if (vehHandle == _audioAnchorVehicle && _isPlaying)
                {
                    _audioAnchorVehicle = -1;
                    _currentVehicleHandle = vehHandle;
                    if (_positional != null) { _positional.Pan = 0f; _positional.Gain = 1f; }
                    ShowStatus("BACK IN THE DRIVER SEAT", 2000, _themeR, _themeG, _themeB);
                }
                else if (vehHandle != _currentVehicleHandle)
                {
                    if (_currentVehicleHandle != -1) SaveVehicleState(_currentVehicleHandle);
                    _currentVehicleHandle = vehHandle;
                    _audioAnchorVehicle = -1;
                    if (_vehicleHistory.ContainsKey(vehHandle))
                    {
                        var state = _vehicleHistory[vehHandle];
                        if (state.WasPlaying || _autoPlayInVehicle)
                        {
                            PlayTrackByPath(state.TrackPath);
                            if (_reader != null) _reader.CurrentTime = state.CurrentTime;
                        }
                        else StopPlayback();
                    }
                    else
                    {
                        if (_autoPlayInVehicle) { if (_currentPlaylist.Count > 0) PlayTrack(_shuffle ? _rng.Next(_currentPlaylist.Count) : _currentIndex); }
                        else if (_shuffle) { if (_currentPlaylist.Count > 0) PlayTrack(_rng.Next(_currentPlaylist.Count)); }
                        else StopPlayback();
                    }
                }
            }
            else
            {
                if (_currentVehicleHandle != -1)
                {
                    SaveVehicleState(_currentVehicleHandle);
                    if (_isPlaying && _positionalAudioEnabled)
                    {
                        _audioAnchorVehicle = _currentVehicleHandle;
                        var exitedVeh = new Vehicle(_audioAnchorVehicle);
                        if (exitedVeh.Exists()) _audioAnchorPos = exitedVeh.Position;
                        UpdatePositionalAudio();
                    }
                    else StopPlayback();
                    _currentVehicleHandle = -1;
                }
            }
        }
        else
        {
            if (inVehicle)
            {
                if (vehHandle != _currentVehicleHandle)
                {
                    _currentVehicleHandle = vehHandle;
                    if (_autoPlayInVehicle && !_isPlaying && _currentPlaylist.Count > 0)
                    {
                        int idx = _currentIndex >= 0 && _currentIndex < _currentPlaylist.Count ? _currentIndex : 0;
                        PlayTrack(_shuffle ? _rng.Next(_currentPlaylist.Count) : idx);
                    }
                }
            }
            else _currentVehicleHandle = -1;
        }

        if (_autoRadioOff && inVehicle)
        {
            Vehicle currentVehRadio = playerPed.CurrentVehicle;
            if (currentVehRadio != null)
            {
                string currentRadio = Function.Call<string>(Hash.GET_PLAYER_RADIO_STATION_NAME);
                if (currentRadio != "OFF") Function.Call(Hash.SET_VEH_RADIO_STATION, currentVehRadio, "OFF");
            }
        }

        if (_isPlaying && _audioAnchorVehicle != -1) UpdatePositionalAudio();

        if (_isPlaying && _reader != null)
        {
            if (_speedVolumeScaling)
            {
                if (playerPed != null && playerPed.IsInVehicle())
                {
                    Vehicle curSpeedVeh = playerPed.CurrentVehicle;
                    if (curSpeedVeh != null) { float speed = curSpeedVeh.Speed; float volOffset = Math.Min(0.2f, (speed / 40f) * 0.2f); _reader.Volume = Math.Min(1f, _volume + volOffset); }
                    else _reader.Volume = _volume;
                }
                else _reader.Volume = _volume;
            }
            else _reader.Volume = _volume;
        }

        if (_pauseOnFocusOrMenu && _gameWindowHandle != IntPtr.Zero)
        {
            IntPtr activeWindow = GetForegroundWindow();
            bool hasFocus = (activeWindow == _gameWindowHandle);
            bool isPausedOrNoFocus = Game.IsPaused || !hasFocus;
            if (isPausedOrNoFocus)
            {
                if (_isPlaying && _output != null && _output.PlaybackState == PlaybackState.Playing)
                {
                    _output.Pause();
                    _autoPaused = true;
                    UpdateSubtitle();
                }
            }
            else
            {
                if (_autoPaused)
                {
                    _autoPaused = false;
                    if (_output != null && _output.PlaybackState == PlaybackState.Paused)
                    {
                        _output.Play();
                        _hudTimer = Game.GameTime + 5000;
                        UpdateSubtitle();
                    }
                }
            }
        }
    }

    private void OnAborted(object sender, EventArgs e) { WriteDefaultConfig(); StopPlayback(); }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var k = e.KeyCode;
        if (k == _keyMenu) _menu.Visible = !_menu.Visible;
        else if (k == _keyPause) TogglePause();
        else if (k == _keyNext && _currentPlaylist.Count > 0)
            PlayTrack(_shuffle ? _rng.Next(_currentPlaylist.Count) : (_currentIndex + 1) % _currentPlaylist.Count);
        else if (k == _keyPrev && _currentPlaylist.Count > 0)
            PlayTrack(_shuffle ? _rng.Next(_currentPlaylist.Count) : (_currentIndex - 1 + _currentPlaylist.Count) % _currentPlaylist.Count);
        else if (k == _keyVolUp) SetVolume(+0.05f);
        else if (k == _keyVolDown) SetVolume(-0.05f);
        else if (k == _keyShuffle)
        {
            _shuffle = !_shuffle;
            _shuffleCheckbox.Checked = _shuffle;
            ShowStatus(_shuffle ? "SHUFFLE: ON" : "SHUFFLE: OFF", 3000, _shuffle ? 0 : 255, _shuffle ? 255 : 100, 100);
            UpdateSubtitle();
        }
        else if (k == _keySeekForward && _reader != null)
        {
            var targetTime = _reader.CurrentTime.Add(TimeSpan.FromSeconds(5));
            if (targetTime > _reader.TotalTime) targetTime = _reader.TotalTime;
            _reader.CurrentTime = targetTime;
            _hudTimer = Game.GameTime + 5000;
            ShowStatus("+5 SECONDS", 2000, _themeR, _themeG, _themeB);
        }
        else if (k == _keySeekBackward && _reader != null)
        {
            var targetTime = _reader.CurrentTime.Subtract(TimeSpan.FromSeconds(5));
            if (targetTime < TimeSpan.Zero) targetTime = TimeSpan.Zero;
            _reader.CurrentTime = targetTime;
            _hudTimer = Game.GameTime + 5000;
            ShowStatus("-5 SECONDS", 2000, _themeR, _themeG, _themeB);
        }
        else if (k == _keyRepeat)
        {
            _repeatMode = (_repeatMode + 1) % _repeatModeNames.Count;
            _repeatListItem.Index = _repeatMode;
            ShowStatus($"REPEAT MODE: {_repeatModeNames[_repeatMode].ToUpper()}", 2500, _themeR, _themeG, _themeB);
            WriteDefaultConfig();
        }
        else if (k == _keyFavorite) ToggleFavoriteCurrent();
        else if (k == _keyVisualizer)
        {
            _showVisualizer = !_showVisualizer;
            _visualizerCheckbox.Checked = _showVisualizer;
            ShowStatus(_showVisualizer ? "VISUALIZER: ON" : "VISUALIZER: OFF", 2500, _themeR, _themeG, _themeB);
            WriteDefaultConfig();
        }
    }

    private class SpectrumTap
    {
        public float[] Buffer = new float[4096];
        public int WritePos = 0;
        public void Push(float sample) { Buffer[WritePos] = sample; WritePos = (WritePos + 1) % Buffer.Length; }
        public float[] SnapshotLatest(int count)
        {
            float[] result = new float[count];
            int start = WritePos - count;
            for (int i = 0; i < count; i++)
            {
                int idx = start + i;
                while (idx < 0) idx += Buffer.Length;
                idx %= Buffer.Length;
                result[i] = Buffer[idx];
            }
            return result;
        }
    }

    private class Positional3DSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _sourceChannels;
        private readonly int _sampleRate;
        private readonly SpectrumTap _spectrumTap;
        private float[] _sourceBuffer = new float[0];
        private float _lpPrevL = 0f;
        private float _lpPrevR = 0f;

        public volatile float Pan = 0f;
        public volatile float Gain = 1f;
        public volatile bool LowPassEnabled = true;
        public WaveFormat WaveFormat { get; private set; }

        public Positional3DSampleProvider(ISampleProvider source, SpectrumTap spectrumTap = null)
        {
            _source = source;
            _sourceChannels = Math.Max(1, source.WaveFormat.Channels);
            _sampleRate = source.WaveFormat.SampleRate;
            _spectrumTap = spectrumTap;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int framesRequested = count / 2;
            int sourceSamplesNeeded = framesRequested * _sourceChannels;
            if (_sourceBuffer.Length < sourceSamplesNeeded) _sourceBuffer = new float[sourceSamplesNeeded];

            int sourceSamplesRead = _source.Read(_sourceBuffer, 0, sourceSamplesNeeded);
            int framesRead = sourceSamplesRead / _sourceChannels;

            float pan = Pan, gain = Gain;
            double angle = (pan + 1.0) * (Math.PI / 4.0);
            float leftGain = (float)Math.Cos(angle) * gain;
            float rightGain = (float)Math.Sin(angle) * gain;

            float cutoffHz = LowPassEnabled ? MathHelperLerp(500f, 22000f, Clamp01(gain)) : 22000f;
            float rc = 1f / (2f * (float)Math.PI * cutoffHz);
            float dt = 1f / _sampleRate;
            float alpha = dt / (rc + dt);

            for (int i = 0; i < framesRead; i++)
            {
                float mono;
                if (_sourceChannels == 1) mono = _sourceBuffer[i];
                else
                {
                    int baseIdx = i * _sourceChannels; float sum = 0f;
                    for (int ch = 0; ch < _sourceChannels; ch++) sum += _sourceBuffer[baseIdx + ch];
                    mono = sum / _sourceChannels;
                }

                if (_spectrumTap != null) _spectrumTap.Push(mono);
                float left = mono * leftGain;
                float right = mono * rightGain;

                _lpPrevL = _lpPrevL + alpha * (left - _lpPrevL);
                _lpPrevR = _lpPrevR + alpha * (right - _lpPrevR);

                buffer[offset + i * 2] = _lpPrevL;
                buffer[offset + i * 2 + 1] = _lpPrevR;
            }
            return framesRead * 2;
        }

        private static float Clamp01(float v) { if (v < 0f) return 0f; if (v > 1f) return 1f; return v; }
        private static float MathHelperLerp(float a, float b, float t) { return a + (b - a) * t; }
    }

    private static class Id3TagReader
    {
        public static void ReadTags(string path, out string title, out string artist)
        {
            title = null;
            artist = null;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] header = new byte[10];
                if (fs.Read(header, 0, 10) == 10 && header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3')
                {
                    int majorVersion = header[3];
                    int size = SynchsafeToInt(header[6], header[7], header[8], header[9]);
                    if (size > 0 && size < fs.Length)
                    {
                        byte[] tagData = new byte[size];
                        fs.Read(tagData, 0, size);
                        ParseId3v2Frames(tagData, majorVersion, out title, out artist);
                    }
                }
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(artist))
                {
                    string v1Title, v1Artist;
                    ReadId3v1(fs, out v1Title, out v1Artist);
                    if (string.IsNullOrEmpty(title)) title = v1Title;
                    if (string.IsNullOrEmpty(artist)) artist = v1Artist;
                }
            }
        }

        private static int SynchsafeToInt(byte b1, byte b2, byte b3, byte b4)
        {
            return ((b1 & 0x7F) << 21) | ((b2 & 0x7F) << 14) | ((b3 & 0x7F) << 7) | (b4 & 0x7F);
        }

        private static void ParseId3v2Frames(byte[] data, int majorVersion, out string title, out string artist)
        {
            title = null;
            artist = null;
            int pos = 0;
            while (pos + 10 <= data.Length)
            {
                string frameId = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                if (frameId == "\0\0\0\0") break;
                int frameSize = majorVersion >= 4
                    ? SynchsafeToInt(data[pos + 4], data[pos + 5], data[pos + 6], data[pos + 7])
                    : (data[pos + 4] << 24) | (data[pos + 5] << 16) | (data[pos + 6] << 8) | data[pos + 7];
                int frameStart = pos + 10;
                if (frameSize <= 0 || frameStart + frameSize > data.Length) break;
                if (frameId == "TIT2" || frameId == "TPE1")
                {
                    string text = DecodeTextFrame(data, frameStart, frameSize);
                    if (frameId == "TIT2") title = text;
                    else artist = text;
                }
                pos = frameStart + frameSize;
                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(artist)) break;
            }
        }

        private static string DecodeTextFrame(byte[] data, int offset, int length)
        {
            if (length <= 1) return null;
            byte encoding = data[offset];
            int textOffset = offset + 1;
            int textLength = length - 1;
            string result;
            try
            {
                switch (encoding)
                {
                    case 1: result = System.Text.Encoding.Unicode.GetString(data, textOffset, textLength); break;
                    case 2: result = System.Text.Encoding.BigEndianUnicode.GetString(data, textOffset, textLength); break;
                    case 3: result = System.Text.Encoding.UTF8.GetString(data, textOffset, textLength); break;
                    default: result = System.Text.Encoding.GetEncoding(28591).GetString(data, textOffset, textLength); break;
                }
            }
            catch { return null; }
            return result.Trim(new char[] { '\0', ' ' });
        }

        private static void ReadId3v1(FileStream fs, out string title, out string artist)
        {
            title = null;
            artist = null;
            if (fs.Length < 128) return;
            byte[] tag = new byte[128];
            fs.Seek(-128, SeekOrigin.End);
            fs.Read(tag, 0, 128);
            string marker = System.Text.Encoding.ASCII.GetString(tag, 0, 3);
            if (marker != "TAG") return;
            title = System.Text.Encoding.ASCII.GetString(tag, 3, 30).Trim(new char[] { '\0', ' ' });
            artist = System.Text.Encoding.ASCII.GetString(tag, 33, 30).Trim(new char[] { '\0', ' ' });
            if (string.IsNullOrEmpty(title)) title = null;
            if (string.IsNullOrEmpty(artist)) artist = null;
        }
    }

    private static class SpectrumAnalyzer
    {
        public const int FftSize = 256;

        public static void ComputeMagnitudes(float[] samples, float[] outMagnitudes)
        {
            int n = FftSize;
            float[] re = new float[n];
            float[] im = new float[n];
            for (int i = 0; i < n; i++)
            {
                float w = 0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * i / (n - 1));
                re[i] = samples[i] * w;
                im[i] = 0f;
            }
            Fft(re, im);
            for (int i = 0; i < n / 2; i++)
                outMagnitudes[i] = (float)Math.Sqrt(re[i] * re[i] + im[i] * im[i]);
        }

        private static void Fft(float[] re, float[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { float tr = re[i]; re[i] = re[j]; re[j] = tr; float ti = im[i]; im[i] = im[j]; im[j] = ti; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                float wr = (float)Math.Cos(ang);
                float wi = (float)Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    float curWr = 1f, curWi = 0f;
                    int half = len / 2;
                    for (int k = 0; k < half; k++)
                    {
                        float ur = re[i + k], ui = im[i + k];
                        float vr = re[i + k + half] * curWr - im[i + k + half] * curWi;
                        float vi = re[i + k + half] * curWi + im[i + k + half] * curWr;
                        re[i + k] = ur + vr; im[i + k] = ui + vi;
                        re[i + k + half] = ur - vr; im[i + k + half] = ui - vi;
                        float nextWr = curWr * wr - curWi * wi;
                        float nextWi = curWr * wi + curWi * wr;
                        curWr = nextWr; curWi = nextWi;
                    }
                }
            }
        }
    }

    private class Float32ToPcm16Provider : IWaveProvider
    {
        private readonly ISampleProvider _source;
        private float[] _sourceBuffer = new float[0];
        public WaveFormat WaveFormat { get; private set; }

        public Float32ToPcm16Provider(ISampleProvider source)
        {
            _source = source;
            WaveFormat = new WaveFormat(source.WaveFormat.SampleRate, 16, source.WaveFormat.Channels);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            int samplesRequested = count / 2;
            if (_sourceBuffer.Length < samplesRequested) _sourceBuffer = new float[samplesRequested];
            int samplesRead = _source.Read(_sourceBuffer, 0, samplesRequested);
            for (int i = 0; i < samplesRead; i++)
            {
                float sample = _sourceBuffer[i];
                if (sample > 1f) sample = 1f;
                else if (sample < -1f) sample = -1f;
                short pcm = (short)(sample * short.MaxValue);
                buffer[offset + i * 2] = (byte)(pcm & 0xFF);
                buffer[offset + i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
            }
            return samplesRead * 2;
        }
    }
}
