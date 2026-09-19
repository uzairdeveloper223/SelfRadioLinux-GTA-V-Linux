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
    private List<string> _tracks = new List<string>();
    private List<string> _filteredTracks = new List<string>();
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
        public int TrackIndex { get; set; }
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
    private UIMenuListItem _themeListItem;
    private UIMenuItem _creditsItem;

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
    private Keys _keyMenu          = Keys.J;                 
    private Keys _keyPause         = Keys.O;                 
    private Keys _keyNext          = Keys.K;                 
    private Keys _keyPrev          = Keys.I;                 
    private Keys _keyVolUp         = Keys.Oemplus;           
    private Keys _keyVolDown       = Keys.OemMinus;          
    private Keys _keyShuffle       = Keys.OemSemicolon;      
    private Keys _keySeekForward   = Keys.OemPeriod;         
    private Keys _keySeekBackward  = Keys.Oemcomma;          

    private string _scriptsDir;
    private string _iniPath;

    public SelfRadioLinux()
    {
        Function.Call((Hash)0xAA391C728106F7AF, false);
        _scriptsDir = Path.GetDirectoryName(this.Filename);
        if (string.IsNullOrEmpty(_scriptsDir) || !Directory.Exists(_scriptsDir))
        {
            _scriptsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts"); 
        }
        _iniPath = Path.Combine(_scriptsDir, "SelfRadioLinux.ini");
        string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _musicDir = Path.Combine(docsPath, "Rockstar Games", "GTA V", "User Music");
        LoadConfig();
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
        _speedVolumeCheckbox = new UIMenuCheckboxItem("Speed Auto-Volume", _speedVolumeScaling, "Slightly increases music volume as your vehicle goes faster to combat engine/road noise.");
        _vehicleOnlyCheckbox = new UIMenuCheckboxItem("Vehicle-Only Playback", _vehicleOnlyPlayback, "When enabled, music only plays inside vehicles and remembers progress per vehicle. When disabled, music plays everywhere.");
        _autoPlayCheckbox = new UIMenuCheckboxItem("Auto-Play Music in Vehicle", _autoPlayInVehicle, "Automatically starts playing your custom music when you enter a vehicle.");
        _positionalAudioCheckbox = new UIMenuCheckboxItem("3D Exterior Audio", _positionalAudioEnabled, "When you exit a vehicle while music is playing, the audio keeps playing from the car's real world position, panned and faded by distance, instead of just stopping.");
        var themesList = new List<object> { "Blue Theme", "Green Theme", "Red Theme", "Orange Theme", "Purple Theme" };
        _themeListItem = new UIMenuListItem("HUD Theme", themesList, _themeIndex, "Change the visual color accent of the HUD and bars.");
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
        _settingsMenu.AddItem(_themeListItem);
        _settingsMenu.AddItem(_creditsItem);
        _settingsMenu.OnCheckboxChange += (sender, item, checkedState) =>
        {
            if (item == _autoRadioOffCheckbox)
            {
                _autoRadioOff = checkedState;
                ShowStatus(_autoRadioOff ? "AUTO RADIO OFF: ENABLED" : "AUTO RADIO OFF: DISABLED", 3000, _themeR, _themeG, _themeB);
            }
            else if (item == _showHudCheckbox)
            {
                _showHud = checkedState;
                ShowStatus(_showHud ? "HUD: ENABLED" : "HUD: DISABLED", 3000, _themeR, _themeG, _themeB);
            }
            else if (item == _shuffleCheckbox)
            {
                _shuffle = checkedState;
                UpdateSubtitle();
                ShowStatus(_shuffle ? "SHUFFLE: ON" : "SHUFFLE: OFF", 3000, _shuffle ? 0 : 255, _shuffle ? 255 : 100, 100);
            }
            else if (item == _pauseOnFocusCheckbox)
            {
                _pauseOnFocusOrMenu = checkedState;
                ShowStatus(_pauseOnFocusOrMenu ? "AUTO-PAUSE: ENABLED" : "AUTO-PAUSE: DISABLED", 3000, _themeR, _themeG, _themeB);
            }
            else if (item == _showProgressCheckbox)
            {
                _showProgressBar = checkedState;
                ShowStatus(_showProgressBar ? "PROGRESS BAR: ENABLED" : "PROGRESS BAR: DISABLED", 3000, _themeR, _themeG, _themeB);
            }
            else if (item == _speedVolumeCheckbox)
            {
                _speedVolumeScaling = checkedState;
                ShowStatus(_speedVolumeScaling ? "SPEED AUTO-VOLUME: ON" : "SPEED AUTO-VOLUME: OFF", 3000, _themeR, _themeG, _themeB);
            }
            else if (item == _vehicleOnlyCheckbox)
            {
                _vehicleOnlyPlayback = checkedState;
                ShowStatus(_vehicleOnlyPlayback ? "VEHICLE PLAYBACK ONLY" : "PLAY EVERYWHERE ENABLED", 3000, _themeR, _themeG, _themeB);
                if (!_vehicleOnlyPlayback)
                {
                    _currentVehicleHandle = -1;
                }
            }
            else if (item == _autoPlayCheckbox)
            {
                _autoPlayInVehicle = checkedState;
                ShowStatus(_autoPlayInVehicle ? "AUTO-PLAY IN VEHICLE: ON" : "AUTO-PLAY IN VEHICLE: OFF", 3000, _themeR, _themeG, _themeB);
            }
            else if (item == _positionalAudioCheckbox)
            {
                _positionalAudioEnabled = checkedState;
                if (!_positionalAudioEnabled && _audioAnchorVehicle != -1)
                {
                    StopPlayback();
                }
                ShowStatus(_positionalAudioEnabled ? "3D EXTERIOR AUDIO: ON" : "3D EXTERIOR AUDIO: OFF", 3000, _themeR, _themeG, _themeB);
            }
            WriteDefaultConfig();
        };
        _settingsMenu.OnListChange += (sender, item, index) =>
        {
            if (item == _themeListItem)
            {
                _themeIndex = index;
                ApplyTheme();
                ShowStatus($"{themesList[index].ToString().ToUpper()} ACTIVE", 3000, _themeR, _themeG, _themeB);
                UpdateSubtitle();
                WriteDefaultConfig();
            }
        };
        _settingsMenu.OnItemSelect += (sender, item, index) =>
        {
            if (item == _creditsItem)
            {
                ShowStatus("DEVELOPED BY UZAIR MUGHAL", 4000, _themeR, _themeG, _themeB);
            }
        };
        _menu.OnItemSelect += (sender, item, index) =>
        {
            if (index == 0)
            {
                TriggerSearch();
            }
            else if (index == _filteredTracks.Count + 1)
            {
            }
            else
            {
                int trackIndex = index - 1; 
                if (trackIndex >= 0 && trackIndex < _filteredTracks.Count)
                {
                    string selectedPath = _filteredTracks[trackIndex];
                    int absIndex = _tracks.IndexOf(selectedPath);
                    if (absIndex >= 0)
                    {
                        PlayTrack(absIndex);
                    }
                }
            }
        };
        Tick    += OnTick;
        KeyUp   += OnKeyUp;
        Aborted += OnAborted;
        LoadTracks();
        if (_tracks.Count > 0)
        {
            if (_currentIndex < 0 || _currentIndex >= _tracks.Count)
            {
                _currentIndex = 0;
            }
            _nowPlaying = SanitizeForGta(Path.GetFileNameWithoutExtension(_tracks[_currentIndex]));
        }
    }

    private void ApplyTheme()
    {
        switch (_themeIndex)
        {
            case 0: _themeR = 0;   _themeG = 150; _themeB = 255; break; 
            case 1: _themeR = 46;  _themeG = 204; _themeB = 113; break; 
            case 2: _themeR = 231; _themeG = 76;  _themeB = 60;  break; 
            case 3: _themeR = 230; _themeG = 126; _themeB = 34;  break; 
            case 4: _themeR = 155; _themeG = 89;  _themeB = 182; break; 
            default: _themeIndex = 0; _themeR = 0; _themeG = 150; _themeB = 255; break;
        }
    }

    private string GetThemeColorCode()
    {
        switch (_themeIndex)
        {
            case 0: return "~b~"; 
            case 1: return "~g~"; 
            case 2: return "~r~"; 
            case 3: return "~o~"; 
            case 4: return "~p~"; 
            default: return "~b~";
        }
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
                case "MusicDir":         _musicDir  = v; break;
                case "Volume":           float.TryParse(v, out _volume); break;
                case "Shuffle":          _shuffle   = v == "1"; break;
                case "ShowHud":          _showHud   = v == "1"; break;
                case "AutoRadioOff":     _autoRadioOff = v == "1"; break;
                case "PauseOnFocus":     _pauseOnFocusOrMenu = v == "1"; break;
                case "ShowProgress":     _showProgressBar = v == "1"; break;
                case "SpeedVolume":      _speedVolumeScaling = v == "1"; break;
                case "VehicleOnly":      _vehicleOnlyPlayback = v == "1"; break;
                case "AutoPlayInVehicle": _autoPlayInVehicle = v == "1"; break;
                case "PositionalAudio":  _positionalAudioEnabled = v == "1"; break;
                case "MaxAudibleDistance": float.TryParse(v, out _maxAudibleDistance); break;
                case "ThemeIndex":       int.TryParse(v, out _themeIndex); break;
                case "CurrentIndex":     int.TryParse(v, out _currentIndex); break;
                case "KeyMenu":          Enum.TryParse<Keys>(v, out _keyMenu);    break;
                case "KeyPause":         Enum.TryParse<Keys>(v, out _keyPause);   break;
                case "KeyNext":          Enum.TryParse<Keys>(v, out _keyNext);    break;
                case "KeyPrev":          Enum.TryParse<Keys>(v, out _keyPrev);    break;
                case "KeyVolUp":         Enum.TryParse<Keys>(v, out _keyVolUp);   break;
                case "KeyVolDown":       Enum.TryParse<Keys>(v, out _keyVolDown); break;
                case "KeyShuffle":       Enum.TryParse<Keys>(v, out _keyShuffle); break;
                case "KeySeekForward":   Enum.TryParse<Keys>(v, out _keySeekForward); break;
                case "KeySeekBackward":  Enum.TryParse<Keys>(v, out _keySeekBackward); break;
            }
        }
    }

    private void WriteDefaultConfig()
    {
        try
        {
            File.WriteAllText(_iniPath,
                "[Settings]\n" +
                $"MusicDir={_musicDir}\n" +
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
                "[Keys]\n" +
                $"KeyMenu={_keyMenu}\n" +
                $"KeyPause={_keyPause}\n" +
                $"KeyNext={_keyNext}\n" +
                $"KeyPrev={_keyPrev}\n" +
                $"KeyVolUp={_keyVolUp}\n" +
                $"KeyVolDown={_keyVolDown}\n" +
                $"KeyShuffle={_keyShuffle}\n" +
                $"KeySeekForward={_keySeekForward}\n" +
                $"KeySeekBackward={_keySeekBackward}\n"
            );
        }
        catch (Exception ex)
        {
            UI.Notify("~r~SelfRadio Config Error: " + ex.Message);
        }
    }

    private void LoadTracks()
    {
        if (!Directory.Exists(_musicDir))
        {
            try
            {
                Directory.CreateDirectory(_musicDir);
            }
            catch (Exception ex)
            {
                UI.Notify("~r~SelfRadio Dir Error: " + ex.Message);
            }
            RefreshMenu();
            return;
        }
        _tracks.Clear();
        var files = new List<string>();
        try
        {
            files.AddRange(Directory.GetFiles(_musicDir, "*.mp3", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(_musicDir, "*.wav", SearchOption.AllDirectories));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            _tracks = files;
            FilterTracks();
        }
        catch (Exception)
        {
            UI.Notify("~r~SelfRadio: Found illegal Windows characters in filename!");
        }
    }

    private void FilterTracks()
    {
        _filteredTracks.Clear();
        if (string.IsNullOrEmpty(_searchFilter))
        {
            _filteredTracks = new List<string>(_tracks);
        }
        else
        {
            foreach (var f in _tracks)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _filteredTracks.Add(f);
                }
            }
        }
        RefreshMenu();
    }

    private void TriggerSearch()
    {
        if (!string.IsNullOrEmpty(_searchFilter))
        {
            _searchFilter = "";
            FilterTracks();
            ShowStatus("SEARCH FILTER CLEARED", 2500, _themeR, _themeG, _themeB);
            return;
        }
        string query = Game.GetUserInput(40);
        if (!string.IsNullOrEmpty(query))
        {
            _searchFilter = query.Trim();
            FilterTracks();
            ShowStatus($"FILTERED: {_filteredTracks.Count} SONGS", 3000, _themeR, _themeG, _themeB);
        }
    }

    private void RefreshMenu()
    {
        _menu.Clear();
        string searchLabel = string.IsNullOrEmpty(_searchFilter) 
            ? "Search Tracks..." 
            : $"Search: \"{_searchFilter}\" ~r~[Clear]";
        UIMenuItem searchBtn = new UIMenuItem(searchLabel, "Click to search songs or clear active filter.");
        _menu.AddItem(searchBtn);
        foreach (var f in _filteredTracks)
        {
            string displayName = SanitizeForGta(Path.GetFileNameWithoutExtension(f));
            if (string.IsNullOrEmpty(displayName)) displayName = "Unknown Track";
            _menu.AddItem(new UIMenuItem(displayName));
        }
        UIMenuItem settingsBtn = new UIMenuItem("Settings Menu", "Configure script options and preferences.");
        _menu.AddItem(settingsBtn);
        _menu.BindMenuToItem(_settingsMenu, settingsBtn);
        UpdateSubtitle();
    }

    private void PlayTrack(int index)
    {
        if (_tracks.Count == 0) return;
        int preservedAnchor = _audioAnchorVehicle;
        StopPlayback();
        _currentIndex = index;
        try
        {
            _output = new WaveOutEvent();
            _reader = new AudioFileReader(_tracks[index]) { Volume = _volume };
            _positional = new Positional3DSampleProvider(_reader);
            var pcmProvider = new Float32ToPcm16Provider(_positional);
            _output.Init(pcmProvider);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Play();
            _isPlaying = true;
            _nowPlaying = SanitizeForGta(Path.GetFileNameWithoutExtension(_tracks[index]));
            _hudTimer   = Game.GameTime + 5000;
            UpdateSubtitle();
            _audioAnchorVehicle = preservedAnchor;
            if (_audioAnchorVehicle != -1)
            {
                UpdatePositionalAudio();
            }
        }
        catch (Exception ex)
        {
            UI.Notify("~r~SelfRadio Error: " + ex.Message);
        }
    }

    private void StopPlayback()
    {
        _isPlaying = false;
        if (_output != null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            try { _output.Stop(); } catch {}
            try { _output.Dispose(); } catch {}
            _output = null;
        }
        if (_reader != null)
        {
            try { _reader.Dispose(); } catch {}
            _reader = null;
        }
        _positional = null;
        _audioAnchorVehicle = -1;
    }

    private void TogglePause()
    {
        if (_output == null && _tracks.Count > 0)
        {
            PlayTrack(_currentIndex);
            return;
        }
        if (_output?.PlaybackState == PlaybackState.Playing)
        {
            _output.Pause();
            _hudTimer = Game.GameTime + 5000; 
        }
        else if (_output?.PlaybackState == PlaybackState.Paused)
        {
            _output.Play();
            _hudTimer = Game.GameTime + 5000; 
        }
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
        if (_audioAnchorVehicle == -1)
        {
            _positional.Pan = 0f;
            _positional.Gain = 1f;
            _distanceHudText = "";
            return;
        }

        GTA.Math.Vector3 sourcePos;
        var anchorVeh = new Vehicle(_audioAnchorVehicle);
        if (anchorVeh.Exists())
        {
            sourcePos = anchorVeh.Position;
            _audioAnchorPos = sourcePos;
        }
        else
        {
            sourcePos = _audioAnchorPos;
        }

        GTA.Math.Vector3 listenerPos = GameplayCamera.Position;
        GTA.Math.Vector3 forward = GameplayCamera.Direction;
        forward.Z = 0f;
        if (forward.LengthSquared() < 0.0001f)
        {
            forward = new GTA.Math.Vector3(0f, 1f, 0f);
        }
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
        if (distance <= _refDistance)
        {
            gain = 1f;
        }
        else if (distance >= _maxAudibleDistance)
        {
            gain = 0f;
        }
        else
        {
            float t = (distance - _refDistance) / (_maxAudibleDistance - _refDistance);
            float linear = 1f - t;
            gain = linear * linear;
        }

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
        foreach (int handle in _vehicleHistory.Keys)
        {
            var veh = new Vehicle(handle);
            if (!veh.Exists())
            {
                toRemove.Add(handle);
            }
        }
        foreach (int handle in toRemove)
        {
            _vehicleHistory.Remove(handle);
        }
    }

    private void SaveVehicleState(int handle)
    {
        VehicleState state = new VehicleState
        {
            TrackIndex = _currentIndex,
            CurrentTime = _reader != null ? _reader.CurrentTime : TimeSpan.Zero,
            WasPlaying = _isPlaying
        };
        _vehicleHistory[handle] = state;
    }

    private string SanitizeForGta(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        string sanitized = input
            .Replace("\u266a", "") 
            .Replace("\u266b", "") 
            .Replace("｜", "|")
            .Replace("：", ":")
            .Trim();
        var sb = new System.Text.StringBuilder();
        foreach (char c in sanitized)
        {
            if (!char.IsControl(c) && !char.IsSurrogate(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Trim();
    }

    private void UpdateSubtitle()
    {
        string colorCode = GetThemeColorCode();
        _menu.Title.Caption = _shuffle ? $"Self Radio {colorCode}[SHUFFLE]" : "Self Radio";
        bool currentlyPlaying = _output?.PlaybackState == PlaybackState.Playing;
        _menu.Subtitle.Caption = _tracks.Count == 0
            ? "~r~No tracks found in music folder"
            : currentlyPlaying
                ? $"{colorCode}PLAYING: ~w~{_nowPlaying}"
                : "~c~Paused / Stopped";
    }

    private void DrawHud()
    {
        int currentTime = Game.GameTime;
        if (currentTime < _volTimer)
        {
            int volRemaining = _volTimer - currentTime;
            int volAlpha = 220;
            int textAlpha = 200;
            int bgAlpha = 150;
            if (volRemaining < 1000) 
            {
                float mult = volRemaining / 1000f;
                volAlpha = (int)(volAlpha * mult);
                textAlpha = (int)(textAlpha * mult);
                bgAlpha = (int)(bgAlpha * mult);
            }
            float bgX = 0.5f;
            float bgY = 0.93f;
            float bgW = 0.15f;
            float bgH = 0.008f;
            Function.Call(Hash.DRAW_RECT, bgX, bgY, bgW, bgH, 0, 0, 0, bgAlpha);
            float fgW = bgW * _volume;
            float fgX = bgX - (bgW / 2f) + (fgW / 2f);
            Function.Call(Hash.DRAW_RECT, fgX, bgY, fgW, bgH, _themeR, _themeG, _themeB, volAlpha);
            Function.Call(Hash.SET_TEXT_FONT, 0);
            Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.25f);
            Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, textAlpha);
            Function.Call(Hash.SET_TEXT_CENTRE, true);
            Function.Call(Hash._SET_TEXT_ENTRY, "STRING");
            Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, $"VOLUME: {(int)(_volume * 100)}%");
            Function.Call(Hash._DRAW_TEXT, bgX, bgY - 0.022f);
        }
        if (currentTime < _statusTimer)
        {
            int shRemaining = _statusTimer - currentTime;
            int shAlpha = 200;
            if (shRemaining < 1000)
            {
                shAlpha = (int)(200 * (shRemaining / 1000f));
            }
            Function.Call(Hash.SET_TEXT_FONT, 0);
            Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.47f);
            Function.Call(Hash.SET_TEXT_COLOUR, _statusColorR, _statusColorG, _statusColorB, shAlpha);
            Function.Call(Hash.SET_TEXT_CENTRE, true);
            Function.Call(Hash._SET_TEXT_ENTRY, "STRING");
            Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, _statusText);
            Function.Call(Hash._DRAW_TEXT, 0.5f, 0.908f);
        }
        if (_showHud && _isPlaying && currentTime < _hudTimer)
        {
            int timeRemaining = _hudTimer - currentTime;
            int alpha = 255;
            if (timeRemaining < 1000) 
            {
                alpha = (int)((timeRemaining / 1000f) * 255f);
                if (alpha < 0) alpha = 0;
            }
            float textX = 0.5f;
            float textY = 0.81f; 
            bool isCurrentlyPlaying = _output?.PlaybackState == PlaybackState.Playing;
            string status = isCurrentlyPlaying ? "NOW PLAYING" : "PAUSED";
            Function.Call(Hash.SET_TEXT_FONT, 4);
            Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.38f); 
            Function.Call(Hash.SET_TEXT_COLOUR, _themeR, _themeG, _themeB, alpha); 
            Function.Call(Hash.SET_TEXT_CENTRE, true);
            Function.Call(Hash._SET_TEXT_ENTRY, "STRING");
            Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, status);
            Function.Call(Hash._DRAW_TEXT, textX, textY);
            Function.Call(Hash.SET_TEXT_FONT, 4);
            Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.62f); 
            Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, alpha);
            Function.Call(Hash.SET_TEXT_CENTRE, true);
            Function.Call(Hash.SET_TEXT_DROPSHADOW, 2, 0, 0, 0, alpha);
            Function.Call(Hash.SET_TEXT_EDGE, 1, 0, 0, 0, (int)(alpha * 0.8f));
            Function.Call(Hash._SET_TEXT_ENTRY, "STRING");
            Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, _nowPlaying);
            Function.Call(Hash._DRAW_TEXT, textX, textY + 0.038f); 
            if (_showProgressBar && _reader != null)
            {
                TimeSpan current = _reader.CurrentTime;
                TimeSpan total = _reader.TotalTime;
                double pct = total.TotalSeconds > 0 ? current.TotalSeconds / total.TotalSeconds : 0;
                float bgX = 0.5f;
                float bgY = textY + 0.088f;
                float bgW = 0.15f;
                float bgH = 0.003f; 
                Function.Call(Hash.DRAW_RECT, bgX, bgY, bgW, bgH, 150, 150, 150, (int)(alpha * 0.3f));
                float fgW = bgW * (float)pct;
                float fgX = bgX - (bgW / 2f) + (fgW / 2f);
                Function.Call(Hash.DRAW_RECT, fgX, bgY, fgW, bgH, _themeR, _themeG, _themeB, alpha);
                string timeStr = $"{current.Minutes:D2}:{current.Seconds:D2} / {total.Minutes:D2}:{total.Seconds:D2}";
                Function.Call(Hash.SET_TEXT_FONT, 0);
                Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.20f);
                Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, alpha);
                Function.Call(Hash.SET_TEXT_CENTRE, true);
                Function.Call(Hash._SET_TEXT_ENTRY, "STRING");
                Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, timeStr);
                Function.Call(Hash._DRAW_TEXT, bgX, bgY + 0.004f);
            }
        }
    }

    private void DrawExteriorAudioHud()
    {
        if (_audioAnchorVehicle == -1 || !_isPlaying || string.IsNullOrEmpty(_distanceHudText)) return;
        Function.Call(Hash.SET_TEXT_FONT, 0);
        Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.30f);
        Function.Call(Hash.SET_TEXT_COLOUR, _themeR, _themeG, _themeB, 210);
        Function.Call(Hash.SET_TEXT_CENTRE, true);
        Function.Call(Hash._SET_TEXT_ENTRY, "STRING");
        Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, _distanceHudText);
        Function.Call(Hash._DRAW_TEXT, 0.5f, 0.955f);
    }

    private void OnPlaybackStopped(object sender, StoppedEventArgs e)
    {
        _trackFinished = true;
    }

    private void OnTick(object sender, EventArgs e)
    {
        _menuPool.ProcessMenus();
        DrawHud();
        DrawExteriorAudioHud();
        if (Game.GameTime > _pruneTimer)
        {
            _pruneTimer = Game.GameTime + 30000;
            PruneVehicleHistory();
        }
        if (_trackFinished)
        {
            _trackFinished = false;
            if (_tracks.Count > 0)
            {
                int next = _shuffle
                    ? _rng.Next(_tracks.Count)
                    : (_currentIndex + 1) % _tracks.Count;
                PlayTrack(next);
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
                    if (_positional != null)
                    {
                        _positional.Pan = 0f;
                        _positional.Gain = 1f;
                    }
                    ShowStatus("BACK IN THE DRIVER SEAT", 2000, _themeR, _themeG, _themeB);
                }
                else if (vehHandle != _currentVehicleHandle)
                {
                    if (_currentVehicleHandle != -1)
                    {
                        SaveVehicleState(_currentVehicleHandle);
                    }
                    _currentVehicleHandle = vehHandle;
                    _audioAnchorVehicle = -1;
                    if (_vehicleHistory.ContainsKey(vehHandle))
                    {
                        var state = _vehicleHistory[vehHandle];
                        if (state.WasPlaying || _autoPlayInVehicle)
                        {
                            PlayTrack(state.TrackIndex);
                            if (_reader != null)
                            {
                                _reader.CurrentTime = state.CurrentTime;
                            }
                        }
                        else
                        {
                            StopPlayback();
                        }
                    }
                    else
                    {
                        if (_autoPlayInVehicle)
                        {
                            if (_tracks.Count > 0) PlayTrack(_shuffle ? _rng.Next(_tracks.Count) : _currentIndex);
                        }
                        else if (_shuffle)
                        {
                            if (_tracks.Count > 0) PlayTrack(_rng.Next(_tracks.Count));
                        }
                        else
                        {
                            StopPlayback(); 
                        }
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
                        if (exitedVeh.Exists())
                        {
                            _audioAnchorPos = exitedVeh.Position;
                        }
                        UpdatePositionalAudio();
                    }
                    else
                    {
                        StopPlayback();
                    }
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
                    if (_autoPlayInVehicle && !_isPlaying && _tracks.Count > 0)
                    {
                        PlayTrack(_shuffle ? _rng.Next(_tracks.Count) : _currentIndex);
                    }
                }
            }
            else
            {
                _currentVehicleHandle = -1; 
            }
        }
        
        if (_autoRadioOff && inVehicle)
        {
            Vehicle currentVehRadio = playerPed.CurrentVehicle;
            if (currentVehRadio != null)
            {
                string currentRadio = Function.Call<string>(Hash.GET_PLAYER_RADIO_STATION_NAME);
                if (currentRadio != "OFF")
                {
                    Function.Call(Hash.SET_VEH_RADIO_STATION, currentVehRadio, "OFF");
                }
            }
        }
        if (_isPlaying && _audioAnchorVehicle != -1)
        {
            UpdatePositionalAudio();
        }
        if (_isPlaying && _reader != null)
        {
            if (_speedVolumeScaling)
            {
                if (playerPed != null && playerPed.IsInVehicle())
                {
                    Vehicle curSpeedVeh = playerPed.CurrentVehicle;
                    if (curSpeedVeh != null)
                    {
                        float speed = curSpeedVeh.Speed; 
                        float volOffset = Math.Min(0.2f, (speed / 40f) * 0.2f);
                        _reader.Volume = Math.Min(1f, _volume + volOffset);
                    }
                    else
                    {
                        _reader.Volume = _volume;
                    }
                }
                else
                {
                    _reader.Volume = _volume;
                }
            }
            else
            {
                _reader.Volume = _volume;
            }
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

    private void OnAborted(object sender, EventArgs e)
    {
        WriteDefaultConfig(); 
        StopPlayback();
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var k = e.KeyCode;
        if      (k == _keyMenu)    _menu.Visible = !_menu.Visible;
        else if (k == _keyPause)   TogglePause();
        else if (k == _keyNext  && _tracks.Count > 0)
            PlayTrack(_shuffle ? _rng.Next(_tracks.Count) : (_currentIndex + 1) % _tracks.Count);
        else if (k == _keyPrev  && _tracks.Count > 0)
            PlayTrack(_shuffle ? _rng.Next(_tracks.Count) : (_currentIndex - 1 + _tracks.Count) % _tracks.Count);
        else if (k == _keyVolUp)   SetVolume(+0.05f); 
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
    }
    private class Positional3DSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _sourceChannels;
        private float[] _sourceBuffer = new float[0];

        public volatile float Pan = 0f;
        public volatile float Gain = 1f;

        public WaveFormat WaveFormat { get; private set; }

        public Positional3DSampleProvider(ISampleProvider source)
        {
            _source = source;
            _sourceChannels = Math.Max(1, source.WaveFormat.Channels);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int framesRequested = count / 2;
            int sourceSamplesNeeded = framesRequested * _sourceChannels;
            if (_sourceBuffer.Length < sourceSamplesNeeded)
            {
                _sourceBuffer = new float[sourceSamplesNeeded];
            }

            int sourceSamplesRead = _source.Read(_sourceBuffer, 0, sourceSamplesNeeded);
            int framesRead = sourceSamplesRead / _sourceChannels;

            float pan = Pan;
            float gain = Gain;
            double angle = (pan + 1.0) * (Math.PI / 4.0);
            float leftGain = (float)Math.Cos(angle) * gain;
            float rightGain = (float)Math.Sin(angle) * gain;

            for (int i = 0; i < framesRead; i++)
            {
                float mono;
                if (_sourceChannels == 1)
                {
                    mono = _sourceBuffer[i];
                }
                else
                {
                    int baseIdx = i * _sourceChannels;
                    float sum = 0f;
                    for (int ch = 0; ch < _sourceChannels; ch++)
                    {
                        sum += _sourceBuffer[baseIdx + ch];
                    }
                    mono = sum / _sourceChannels;
                }
                buffer[offset + i * 2]     = mono * leftGain;
                buffer[offset + i * 2 + 1] = mono * rightGain;
            }

            return framesRead * 2;
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
            if (_sourceBuffer.Length < samplesRequested)
            {
                _sourceBuffer = new float[samplesRequested];
            }

            int samplesRead = _source.Read(_sourceBuffer, 0, samplesRequested);
            for (int i = 0; i < samplesRead; i++)
            {
                float sample = _sourceBuffer[i];
                if (sample > 1f) sample = 1f;
                else if (sample < -1f) sample = -1f;
                short pcm = (short)(sample * short.MaxValue);
                buffer[offset + i * 2]     = (byte)(pcm & 0xFF);
                buffer[offset + i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
            }

            return samplesRead * 2;
        }
    }
}
