using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using ILogService = EasyCon.Core.Services.ILogService;
using Resources = EasyCon2.UI.Common.Properties.Resources;

namespace EasyCon2.Avalonia.Connection;

public partial class ESPConfigViewModel : ViewModelBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILogService _logService;
    private readonly IDialogService _dialogService;
    private Dictionary<string, List<AmiiboInfo>> _amiibosDict = new();
    private List<AmiiboInfo> _allAmiibos = new();
    private static readonly string AmiiboDir = Path.Combine(AppContext.BaseDirectory, "Amiibo");

    #region 公共

    [ObservableProperty]
    private string _statusMessage = "就绪";

    private bool IsDeviceConnected => _deviceService.IsConnected;

    public ESPConfigViewModel(IDeviceService deviceService, ILogService logService, IDialogService dialogService)
    {
        _deviceService = deviceService;
        _logService = logService;
        _dialogService = dialogService;
        InitializeAmiiboData();
    }

    #endregion

    #region 手柄模式

    // 1=JoyCon-L, 2=JoyCon-R, 3=Pro
    [ObservableProperty]
    private byte _selectedMode = 3;

    public List<byte> ControllerModes { get; } = [3, 2, 1];
    public List<string> ControllerModeNames { get; } = ["Pro", "JoyCon-R", "JoyCon-L"];

    [RelayCommand]
    private async Task SetModeAsync()
    {
        if (!IsDeviceConnected)
        {
            StatusMessage = "串口未连接";
            return;
        }

        var ns = _deviceService.GetDevice();
        // 同步串口 I/O（分包重试）放后台执行，避免冻结 UI；续体经 await 回到 UI 线程
        var ok = await Task.Run(() => ns.ChangeControllerMode(SelectedMode));
        StatusMessage = ok ? "手柄模式修改成功，请重启手柄后查看效果" : "手柄模式修改失败";
        _logService.AddLog(ok ? "手柄模式修改成功" : "手柄模式修改失败");
    }

    #endregion

    #region 外观颜色

    [ObservableProperty]
    private Color _bodyColor = Colors.Black;

    [ObservableProperty]
    private Color _buttonColor = Colors.White;

    [ObservableProperty]
    private Color _gripLColor = Colors.DodgerBlue;

    [ObservableProperty]
    private Color _gripRColor = Colors.Yellow;

    [RelayCommand]
    private async Task SetColorAsync()
    {
        if (!IsDeviceConnected)
        {
            StatusMessage = "串口未连接";
            return;
        }

        byte[] color =
        [
            BodyColor.R, BodyColor.G, BodyColor.B,
            ButtonColor.R, ButtonColor.G, ButtonColor.B,
            GripLColor.R, GripLColor.G, GripLColor.B,
            GripRColor.R, GripRColor.G, GripRColor.B,
        ];

        var ns = _deviceService.GetDevice();
        var ok = await Task.Run(() => ns.ChangeControllerColor(color));
        StatusMessage = ok ? "手柄颜色修改成功，请重启手柄后查看效果" : "手柄颜色修改失败";
        _logService.AddLog(ok ? "手柄颜色修改成功" : "手柄颜色修改失败");
    }

    [RelayCommand]
    private async Task PickBodyColorAsync() => BodyColor = await PickColorAsync(BodyColor);

    [RelayCommand]
    private async Task PickButtonColorAsync() => ButtonColor = await PickColorAsync(ButtonColor);

    [RelayCommand]
    private async Task PickGripLColorAsync() => GripLColor = await PickColorAsync(GripLColor);

    [RelayCommand]
    private async Task PickGripRColorAsync() => GripRColor = await PickColorAsync(GripRColor);

    private async Task<Color> PickColorAsync(Color current)
    {
        var result = await _dialogService.PickColorAsync(new DialogColor(current.A, current.R, current.G, current.B));
        return result == null ? current : Color.FromArgb(result.A, result.R, result.G, result.B);
    }

    #endregion

    #region Amiibo

    [ObservableProperty]
    private ObservableCollection<string> _gameList = new();

    [ObservableProperty]
    private string? _selectedGame;

    [ObservableProperty]
    private ObservableCollection<AmiiboInfo> _amiiboList = new();

    [ObservableProperty]
    private AmiiboInfo? _selectedAmiibo;

    [ObservableProperty]
    private bool _hasAmiiboItems;

    public List<string> SaveIndexList { get; } =
        Enumerable.Range(0, 20).Select(i => i.ToString()).ToList();

    [ObservableProperty]
    private int _selectedSaveIndex;

    [ObservableProperty]
    private string _amiiboNickname = "";

    [ObservableProperty]
    private string _username = "EasyCon";

    [ObservableProperty]
    private int _currentAmiiboIndex;

    [ObservableProperty]
    private Bitmap? _amiiboPreviewImage;

    partial void OnAmiiboPreviewImageChanged(Bitmap? oldValue, Bitmap? newValue)
    {
        // 更换预览图后释放旧的原生位图（Amiibo 选择会反复触发）
        (oldValue as IDisposable)?.Dispose();
    }

    partial void OnSelectedGameChanged(string? value)
    {
        if (value == null) return;
        AmiiboList.Clear();
        AmiiboPreviewImage = null;
        SelectedAmiibo = null;
        HasAmiiboItems = false;

        if (value == "自定义")
        {
            LoadCustomAmiibos();
            return;
        }

        if (_amiibosDict.TryGetValue(value, out var list))
        {
            foreach (var amiibo in list)
                AmiiboList.Add(amiibo);
            HasAmiiboItems = AmiiboList.Count > 0;
        }
    }

    partial void OnSelectedAmiiboChanged(AmiiboInfo? value)
    {
        if (value == null) return;

        if (SelectedGame == "自定义")
        {
            LoadCustomAmiiboPreview(value.Name);
            return;
        }

        AmiiboNickname = value.Name.Replace(" ", "");
        LoadAmiiboImage(value);
    }

    [RelayCommand]
    private async Task SaveAmiiboAsync()
    {
        if (!IsDeviceConnected)
        {
            StatusMessage = "串口未连接";
            return;
        }

        if (SelectedSaveIndex >= 20)
        {
            StatusMessage = "请选择存储位置";
            return;
        }

        if (SelectedAmiibo == null)
        {
            StatusMessage = "请选择Amiibo";
            return;
        }

        try
        {
            byte[] data;
            if (SelectedGame == "自定义")
            {
                var filePath = Path.Combine(AmiiboDir, SelectedAmiibo.Name);
                if (!File.Exists(filePath))
                {
                    StatusMessage = "Amiibo文件不存在";
                    return;
                }
                data = File.ReadAllBytes(filePath);
                if (data.Length != 540)
                {
                    StatusMessage = "Amiibo文件长度不正确（需要540字节）";
                    return;
                }
            }
            else
            {
                // Amiibo生成功能暂未实现（依赖LibAmiibo原生DLL）
                StatusMessage = "Amiibo自动生成功能暂未实现，请使用自定义bin文件";
                return;
            }

            var ns = _deviceService.GetDevice();
            var index = (byte)SelectedSaveIndex;
            var ok = await Task.Run(() => ns.SaveAmiibo(index, data));
            StatusMessage = ok ? $"Amiibo已保存到槽位 {index}" : "Amiibo存储失败";
            _logService.AddLog(ok ? $"Amiibo已保存到槽位 {index}" : "Amiibo存储失败");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Amiibo存储失败: {ex.Message}";
            _logService.AddLog($"Amiibo存储失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ActivateAmiiboAsync()
    {
        if (!IsDeviceConnected)
        {
            StatusMessage = "串口未连接";
            return;
        }

        if (CurrentAmiiboIndex >= 20)
        {
            StatusMessage = "序号范围 0-19";
            return;
        }

        var ns = _deviceService.GetDevice();
        var index = (byte)CurrentAmiiboIndex;
        var ok = await Task.Run(() => ns.ChangeAmiiboIndex(index));
        StatusMessage = ok ? $"已激活 Amiibo 槽位 {index}" : "Amiibo激活失败";
        _logService.AddLog(ok ? $"已激活 Amiibo 槽位 {index}" : "Amiibo激活失败");
    }

    #endregion

    #region 数据加载

    private void InitializeAmiiboData()
    {
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(Resources.Amiibo);
            _allAmiibos = JsonSerializer.Deserialize<List<AmiiboInfo>>(json) ?? new();
            _amiibosDict = new Dictionary<string, List<AmiiboInfo>>();

            foreach (var am in _allAmiibos)
            {
                if (string.IsNullOrEmpty(am.GameSeries)) continue;
                if (!_amiibosDict.ContainsKey(am.GameSeries))
                {
                    _amiibosDict[am.GameSeries] = new List<AmiiboInfo>();
                    GameList.Add(am.GameSeries);
                }
                _amiibosDict[am.GameSeries].Add(am);
            }

            GameList.Add("自定义");
            _logService.AddLog($"已加载 {_allAmiibos.Count} 个Amiibo，{_amiibosDict.Count} 个游戏系列");
        }
        catch (Exception ex)
        {
            _logService.AddLog($"加载Amiibo数据失败: {ex.Message}");
            StatusMessage = "加载Amiibo数据失败";
        }
    }

    private void LoadCustomAmiibos()
    {
        if (!Directory.Exists(AmiiboDir))
        {
            Directory.CreateDirectory(AmiiboDir);
            StatusMessage = "Amiibo目录已创建，请将bin文件放入Amiibo文件夹";
            HasAmiiboItems = false;
            return;
        }

        AmiiboList.Clear();
        foreach (var file in Directory.GetFiles(AmiiboDir, "*.bin"))
        {
            AmiiboList.Add(new AmiiboInfo
            {
                Name = Path.GetFileName(file),
                GameSeries = "自定义"
            });
        }
        HasAmiiboItems = AmiiboList.Count > 0;
    }

    private void LoadAmiiboImage(AmiiboInfo amiibo)
    {
        if (string.IsNullOrEmpty(amiibo.Image)) return;
        var imageName = amiibo.Image.Split('/').Last().Replace("png", "jpg");
        var imagePath = Path.Combine(AmiiboDir, "AmiiboImages", imageName);
        if (!File.Exists(imagePath))
        {
            AmiiboPreviewImage = null;
            return;
        }

        try
        {
            var bitmap = new Bitmap(imagePath);
            AmiiboPreviewImage = bitmap;
        }
        catch
        {
            AmiiboPreviewImage = null;
        }
    }

    private void LoadCustomAmiiboPreview(string fileName)
    {
        var filePath = Path.Combine(AmiiboDir, fileName);
        if (!File.Exists(filePath)) return;

        try
        {
            var data = File.ReadAllBytes(filePath);
            if (data.Length < 92) return;

            string head = data[84].ToString("x2") + data[85].ToString("x2")
                        + data[86].ToString("x2") + data[87].ToString("x2");
            string tail = data[88].ToString("x2") + data[89].ToString("x2")
                        + data[90].ToString("x2") + data[91].ToString("x2");

            foreach (var am in _allAmiibos)
            {
                if (am.Head == head && am.Tail == tail)
                {
                    AmiiboNickname = am.Name.Replace(" ", "");
                    LoadAmiiboImage(am);
                    return;
                }
            }

            AmiiboPreviewImage = null;
        }
        catch
        {
            AmiiboPreviewImage = null;
        }
    }

    #endregion
}