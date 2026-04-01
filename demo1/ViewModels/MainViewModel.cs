using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using demo1.Infrastructure;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace demo1.ViewModels
{
    public sealed class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly SerialPort _serialPort = new SerialPort();
        private readonly object _readLock = new object();
        private readonly DispatcherTimer _autoSendTimer;

        private string _selectedPort;
        private int _selectedBaudRate = 9600;
        private Parity _selectedParity = Parity.None;
        private int _selectedDataBits = 8;
        private StopBits _selectedStopBits = StopBits.One;

        private bool _rtsEnabled;
        private bool _dtrEnabled;
        private bool _receiveHex;
        private bool _sendHex;
        private bool _autoClear;
        private bool _autoSendEnabled;
        private int _autoSendInterval = 1000;
        private bool _isReceivePaused;

        private string _receiveLog = string.Empty;
        private string _sendInput = string.Empty;
        private string _saveDirectory = string.Empty;
        private string _sendFilePath = string.Empty;
        private string _statusMessage = "准备就绪";

        private int _sendCount;
        private int _receiveCount;

        private const int MaxPreviewBytes = 4000;

        public MainViewModel()
        {
            Ports = new ObservableCollection<string>();
            BaudRates = new ObservableCollection<int> { 9600, 19200, 38400, 57600, 115200 };
            ParityOptions = new ObservableCollection<Parity> { Parity.None, Parity.Odd, Parity.Even };
            DataBitsOptions = new ObservableCollection<int> { 8, 7, 6, 5 };
            StopBitsOptions = new ObservableCollection<StopBits> { StopBits.One, StopBits.OnePointFive, StopBits.Two };

            TogglePortCommand = new RelayCommand(_ => TogglePort());
            RefreshPortsCommand = new RelayCommand(_ => LoadPorts());
            ClearReceiveCommand = new RelayCommand(_ => ClearReceive());
            ToggleReceivePauseCommand = new RelayCommand(_ => ToggleReceivePause());
            SaveDataCommand = new RelayCommand(_ => SaveData());
            ChooseSavePathCommand = new RelayCommand(_ => ChooseSavePath());

            SendCommand = new RelayCommand(_ => SendText());
            ClearSendCommand = new RelayCommand(_ => SendInput = string.Empty);
            ChooseFileCommand = new RelayCommand(_ => ChooseFile());
            SendFileCommand = new RelayCommand(_ => SendFile());
            ClearCountCommand = new RelayCommand(_ => ClearCounters());

            _autoSendTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_autoSendInterval)
            };
            _autoSendTimer.Tick += (sender, args) => SendText();

            _serialPort.DataReceived += SerialPort_DataReceived;

            LoadPorts();
        }

        public ObservableCollection<string> Ports { get; }
        public ObservableCollection<int> BaudRates { get; }
        public ObservableCollection<Parity> ParityOptions { get; }
        public ObservableCollection<int> DataBitsOptions { get; }
        public ObservableCollection<StopBits> StopBitsOptions { get; }

        public ICommand TogglePortCommand { get; }
        public ICommand RefreshPortsCommand { get; }
        public ICommand ClearReceiveCommand { get; }
        public ICommand ToggleReceivePauseCommand { get; }
        public ICommand SaveDataCommand { get; }
        public ICommand ChooseSavePathCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand ClearSendCommand { get; }
        public ICommand ChooseFileCommand { get; }
        public ICommand SendFileCommand { get; }
        public ICommand ClearCountCommand { get; }

        public string SelectedPort
        {
            get { return _selectedPort; }
            set { SetProperty(ref _selectedPort, value); }
        }

        public int SelectedBaudRate
        {
            get { return _selectedBaudRate; }
            set { SetProperty(ref _selectedBaudRate, value); }
        }

        public Parity SelectedParity
        {
            get { return _selectedParity; }
            set { SetProperty(ref _selectedParity, value); }
        }

        public int SelectedDataBits
        {
            get { return _selectedDataBits; }
            set { SetProperty(ref _selectedDataBits, value); }
        }

        public StopBits SelectedStopBits
        {
            get { return _selectedStopBits; }
            set { SetProperty(ref _selectedStopBits, value); }
        }

        public bool RtsEnabled
        {
            get { return _rtsEnabled; }
            set
            {
                if (SetProperty(ref _rtsEnabled, value) && _serialPort.IsOpen)
                {
                    _serialPort.RtsEnable = value;
                }
            }
        }

        public bool DtrEnabled
        {
            get { return _dtrEnabled; }
            set
            {
                if (SetProperty(ref _dtrEnabled, value) && _serialPort.IsOpen)
                {
                    _serialPort.DtrEnable = value;
                }
            }
        }

        public bool ReceiveHex
        {
            get { return _receiveHex; }
            set { SetProperty(ref _receiveHex, value); }
        }

        public bool SendHex
        {
            get { return _sendHex; }
            set { SetProperty(ref _sendHex, value); }
        }

        public bool AutoClear
        {
            get { return _autoClear; }
            set { SetProperty(ref _autoClear, value); }
        }

        public bool AutoSendEnabled
        {
            get { return _autoSendEnabled; }
            set
            {
                if (!SetProperty(ref _autoSendEnabled, value))
                {
                    return;
                }

                if (value)
                {
                    if (string.IsNullOrWhiteSpace(SendInput))
                    {
                        StatusMessage = "自动发送内容不能为空";
                        _autoSendEnabled = false;
                        OnPropertyChanged();
                        return;
                    }

                    _autoSendTimer.Start();
                }
                else
                {
                    _autoSendTimer.Stop();
                }
            }
        }

        public int AutoSendInterval
        {
            get { return _autoSendInterval; }
            set
            {
                if (value <= 0)
                {
                    value = 1000;
                }

                if (SetProperty(ref _autoSendInterval, value))
                {
                    _autoSendTimer.Interval = TimeSpan.FromMilliseconds(value);
                }
            }
        }

        public bool IsReceivePaused
        {
            get { return _isReceivePaused; }
            set
            {
                if (SetProperty(ref _isReceivePaused, value))
                {
                    OnPropertyChanged(nameof(PauseButtonText));
                }
            }
        }

        public string PauseButtonText => IsReceivePaused ? "继续接收" : "暂停接收";

        public string ReceiveLog
        {
            get { return _receiveLog; }
            set { SetProperty(ref _receiveLog, value); }
        }

        public string SendInput
        {
            get { return _sendInput; }
            set { SetProperty(ref _sendInput, value); }
        }

        public string SaveDirectory
        {
            get { return _saveDirectory; }
            set { SetProperty(ref _saveDirectory, value); }
        }

        public string SendFilePath
        {
            get { return _sendFilePath; }
            set { SetProperty(ref _sendFilePath, value); }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set { SetProperty(ref _statusMessage, value); }
        }

        public int SendCount
        {
            get { return _sendCount; }
            set { SetProperty(ref _sendCount, value); }
        }

        public int ReceiveCount
        {
            get { return _receiveCount; }
            set { SetProperty(ref _receiveCount, value); }
        }

        public bool IsPortOpen => _serialPort.IsOpen;

        public string OpenButtonText => IsPortOpen ? "关闭串口" : "打开串口";

        private void LoadPorts()
        {
            Ports.Clear();

            foreach (var port in SerialPort.GetPortNames().OrderBy(p => p))
            {
                Ports.Add(port);
            }

            if (Ports.Count == 0)
            {
                using (var keyCom = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
                {
                    var valueNames = keyCom?.GetValueNames() ?? Array.Empty<string>();
                    foreach (var valueName in valueNames)
                    {
                        if (keyCom?.GetValue(valueName) is string name && !Ports.Contains(name))
                        {
                            Ports.Add(name);
                        }
                    }
                }
            }

            if (Ports.Count > 0 && (string.IsNullOrWhiteSpace(SelectedPort) || !Ports.Contains(SelectedPort)))
            {
                SelectedPort = Ports[0];
            }

            StatusMessage = Ports.Count > 0 ? $"已加载{Ports.Count}个串口" : "未检测到串口";
        }

        private void TogglePort()
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                    StatusMessage = "串口已关闭";
                    OnPropertyChanged(nameof(IsPortOpen));
                    OnPropertyChanged(nameof(OpenButtonText));
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedPort))
                {
                    StatusMessage = "请先选择串口";
                    return;
                }

                _serialPort.PortName = SelectedPort;
                _serialPort.BaudRate = SelectedBaudRate;
                _serialPort.DataBits = SelectedDataBits;
                _serialPort.Parity = SelectedParity;
                _serialPort.StopBits = SelectedStopBits;
                _serialPort.RtsEnable = RtsEnabled;
                _serialPort.DtrEnable = DtrEnabled;
                _serialPort.Open();

                StatusMessage = $"串口 {SelectedPort} 已打开";
                OnPropertyChanged(nameof(IsPortOpen));
                OnPropertyChanged(nameof(OpenButtonText));
            }
            catch (Exception ex)
            {
                StatusMessage = $"串口操作失败: {ex.Message}";
            }
        }

        private void ToggleReceivePause()
        {
            IsReceivePaused = !IsReceivePaused;
            StatusMessage = IsReceivePaused ? "已暂停接收显示" : "恢复接收显示";
        }

        private void ClearReceive()
        {
            ReceiveLog = string.Empty;
            ReceiveCount = 0;
        }

        private void ClearCounters()
        {
            SendCount = 0;
            ReceiveCount = 0;
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                lock (_readLock)
                {
                    var bytes = _serialPort.BytesToRead;
                    if (bytes <= 0)
                    {
                        return;
                    }

                    var buffer = new byte[bytes];
                    _serialPort.Read(buffer, 0, bytes);

                    var text = ReceiveHex
                        ? Transform.HexToString(buffer, " ")
                        : Encoding.GetEncoding("GBK").GetString(buffer).Replace("\0", "\\0");

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        ReceiveCount += bytes;
                        if (!IsReceivePaused)
                        {
                            AppendLog("RX", text);
                        }

                        if (AutoClear && ReceiveLog.Length > 12000)
                        {
                            ReceiveLog = string.Empty;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() => StatusMessage = $"接收异常: {ex.Message}");
            }
        }

        private void SendText()
        {
            try
            {
                if (!_serialPort.IsOpen)
                {
                    StatusMessage = "串口未打开";
                    return;
                }

                var input = SendInput?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(input))
                {
                    StatusMessage = "发送内容不能为空";
                    return;
                }

                byte[] payload;
                if (SendHex)
                {
                    payload = Transform.HexToBytes(input.Replace(" ", string.Empty));
                }
                else
                {
                    payload = Encoding.GetEncoding("GBK").GetBytes(input);
                }

                _serialPort.Write(payload, 0, payload.Length);
                SendCount += payload.Length;

                var display = SendHex ? Transform.HexToString(payload, " ") : Encoding.GetEncoding("GBK").GetString(payload);
                AppendLog("TX", display);
            }
            catch (Exception ex)
            {
                StatusMessage = $"发送失败: {ex.Message}";
            }
        }

        private void SaveData()
        {
            if (string.IsNullOrWhiteSpace(ReceiveLog))
            {
                StatusMessage = "没有可保存数据";
                return;
            }

            if (string.IsNullOrWhiteSpace(SaveDirectory))
            {
                StatusMessage = "请先选择保存路径";
                return;
            }

            try
            {
                Directory.CreateDirectory(SaveDirectory);
                var fileName = Path.Combine(SaveDirectory, $"Data_{DateTime.Now:yyyyMMddHHmmss}.txt");
                File.WriteAllText(fileName, ReceiveLog, Encoding.GetEncoding("GBK"));
                StatusMessage = $"保存成功: {fileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
            }
        }

        private void ChooseSavePath()
        {
            var dialog = new CommonOpenFileDialog
            {
                Title = "选择保存路径",
                IsFolderPicker = true,
                InitialDirectory = SaveDirectory,
                EnsurePathExists = true
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                SaveDirectory = dialog.FileName;
            }
        }

        private void ChooseFile()
        {
            var dialog = new CommonOpenFileDialog
            {
                Title = "选择发送文件",
                EnsureFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            {
                return;
            }

            SendFilePath = dialog.FileName;

            try
            {
                var data = File.ReadAllBytes(SendFilePath);
                var preview = data.Take(MaxPreviewBytes).ToArray();
                var previewText = BitConverter.ToString(preview).Replace("-", " ");
                AppendLog("FILE", $"{Path.GetFileName(SendFilePath)} 预览({preview.Length} bytes): {previewText}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"文件预览失败: {ex.Message}";
            }
        }

        private void SendFile()
        {
            try
            {
                if (!_serialPort.IsOpen)
                {
                    StatusMessage = "串口未打开";
                    return;
                }

                if (string.IsNullOrWhiteSpace(SendFilePath) || !File.Exists(SendFilePath))
                {
                    StatusMessage = "请先选择有效文件";
                    return;
                }

                var fileData = File.ReadAllBytes(SendFilePath);
                _serialPort.Write(fileData, 0, fileData.Length);
                SendCount += fileData.Length;
                StatusMessage = $"文件发送成功: {Path.GetFileName(SendFilePath)} ({fileData.Length} 字节)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"发送文件失败: {ex.Message}";
            }
        }

        private void AppendLog(string tag, string text)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{tag}] {text}{Environment.NewLine}";
            ReceiveLog += line;
        }

        public void Dispose()
        {
            _autoSendTimer.Stop();
            _serialPort.DataReceived -= SerialPort_DataReceived;
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            _serialPort.Dispose();
        }
    }
}
