using Microsoft.WindowsAPICodePack.Dialogs;
using demo1.Commands;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace demo1.ViewModels
{
    public sealed class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly SerialPort _serialPort;
        private readonly DispatcherTimer _autoSendTimer;

        private string _selectedPort;
        private int _selectedBaudRate = 9600;
        private string _selectedParity = "NONE";
        private int _selectedDataBits = 8;
        private string _selectedStopBits = "1";
        private bool _rtsEnabled;
        private bool _dtrEnabled;

        private string _receiveText = string.Empty;
        private string _sendText = string.Empty;
        private string _saveDirectory = string.Empty;
        private string _sendFilePath = string.Empty;
        private string _statusText = "就绪";

        private bool _isPortOpen;
        private bool _autoClearEnabled;
        private bool _receiveHexMode;
        private bool _sendHexMode;
        private bool _autoSendEnabled;
        private bool _isReceivePaused;
        private int _sendCount;
        private int _receiveCount;
        private int _autoSendInterval = 1000;

        private const int MaxPreviewBytes = 4000;
        private const int ReceiveAutoClearThreshold = 40000;

        public MainViewModel()
        {
            _serialPort = new SerialPort();
            _serialPort.DataReceived += SerialPort_DataReceived;

            _autoSendTimer = new DispatcherTimer();
            _autoSendTimer.Interval = TimeSpan.FromMilliseconds(_autoSendInterval);
            _autoSendTimer.Tick += AutoSendTimer_Tick;

            BaudRates = new ObservableCollection<int> { 9600, 19200, 38400, 57600, 115200 };
            ParityOptions = new ObservableCollection<string> { "NONE", "ODD", "EVEN" };
            DataBitsOptions = new ObservableCollection<int> { 8, 7, 6, 5 };
            StopBitsOptions = new ObservableCollection<string> { "1", "1.5", "2" };
            PortNames = new ObservableCollection<string>();

            RefreshPortsCommand = new RelayCommand(LoadPorts);
            TogglePortCommand = new RelayCommand(TogglePort);
            SendCommand = new RelayCommand(SendTextData);
            ClearReceiveCommand = new RelayCommand(ClearReceive);
            ClearSendCommand = new RelayCommand(() => SendText = string.Empty);
            ClearCountCommand = new RelayCommand(ClearCount);
            SavePathCommand = new RelayCommand(ChooseSavePath);
            SaveDataCommand = new RelayCommand(SaveReceiveData);
            OpenFileCommand = new RelayCommand(OpenFile);
            SendFileCommand = new RelayCommand(SendFile);
            TogglePauseCommand = new RelayCommand(() => IsReceivePaused = !IsReceivePaused);

            LoadPorts();
        }

        public ObservableCollection<string> PortNames { get; }
        public ObservableCollection<int> BaudRates { get; }
        public ObservableCollection<string> ParityOptions { get; }
        public ObservableCollection<int> DataBitsOptions { get; }
        public ObservableCollection<string> StopBitsOptions { get; }

        public RelayCommand RefreshPortsCommand { get; }
        public RelayCommand TogglePortCommand { get; }
        public RelayCommand SendCommand { get; }
        public RelayCommand ClearReceiveCommand { get; }
        public RelayCommand ClearSendCommand { get; }
        public RelayCommand ClearCountCommand { get; }
        public RelayCommand SavePathCommand { get; }
        public RelayCommand SaveDataCommand { get; }
        public RelayCommand OpenFileCommand { get; }
        public RelayCommand SendFileCommand { get; }
        public RelayCommand TogglePauseCommand { get; }

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

        public string SelectedParity
        {
            get { return _selectedParity; }
            set { SetProperty(ref _selectedParity, value); }
        }

        public int SelectedDataBits
        {
            get { return _selectedDataBits; }
            set { SetProperty(ref _selectedDataBits, value); }
        }

        public string SelectedStopBits
        {
            get { return _selectedStopBits; }
            set { SetProperty(ref _selectedStopBits, value); }
        }

        public bool RtsEnabled
        {
            get { return _rtsEnabled; }
            set
            {
                if (!SetProperty(ref _rtsEnabled, value)) return;
                if (_serialPort.IsOpen) _serialPort.RtsEnable = value;
            }
        }

        public bool DtrEnabled
        {
            get { return _dtrEnabled; }
            set
            {
                if (!SetProperty(ref _dtrEnabled, value)) return;
                if (_serialPort.IsOpen) _serialPort.DtrEnable = value;
            }
        }

        public bool IsPortOpen
        {
            get { return _isPortOpen; }
            private set
            {
                if (SetProperty(ref _isPortOpen, value))
                {
                    OnPropertyChanged(nameof(TogglePortText));
                }
            }
        }

        public string TogglePortText
        {
            get { return IsPortOpen ? "关闭串口" : "打开串口"; }
        }

        public string ReceiveText
        {
            get { return _receiveText; }
            set { SetProperty(ref _receiveText, value); }
        }

        public string SendText
        {
            get { return _sendText; }
            set { SetProperty(ref _sendText, value); }
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

        public string StatusText
        {
            get { return _statusText; }
            set { SetProperty(ref _statusText, value); }
        }

        public bool AutoClearEnabled
        {
            get { return _autoClearEnabled; }
            set { SetProperty(ref _autoClearEnabled, value); }
        }

        public bool ReceiveHexMode
        {
            get { return _receiveHexMode; }
            set { SetProperty(ref _receiveHexMode, value); }
        }

        public bool SendHexMode
        {
            get { return _sendHexMode; }
            set { SetProperty(ref _sendHexMode, value); }
        }

        public bool AutoSendEnabled
        {
            get { return _autoSendEnabled; }
            set
            {
                if (!SetProperty(ref _autoSendEnabled, value)) return;
                if (value)
                {
                    if (string.IsNullOrWhiteSpace(SendText))
                    {
                        MessageBox.Show("自动发送内容不能为空");
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

        public bool IsReceivePaused
        {
            get { return _isReceivePaused; }
            set
            {
                if (SetProperty(ref _isReceivePaused, value))
                {
                    OnPropertyChanged(nameof(PauseButtonText));
                    if (!value)
                    {
                        AppendLine("INFO", "恢复显示");
                    }
                }
            }
        }

        public string PauseButtonText
        {
            get { return IsReceivePaused ? "继续接收" : "暂停接收"; }
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

        public void LoadPorts()
        {
            PortNames.Clear();
            foreach (var port in SerialPort.GetPortNames().OrderBy(p => p))
            {
                PortNames.Add(port);
            }

            if (PortNames.Count > 0 && string.IsNullOrWhiteSpace(SelectedPort))
            {
                SelectedPort = PortNames[0];
            }

            StatusText = PortNames.Count > 0 ? "串口列表已刷新" : "未检测到串口";
        }

        private void TogglePort()
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                    IsPortOpen = false;
                    StatusText = "串口已关闭";
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedPort))
                {
                    StatusText = "请先选择串口";
                    return;
                }

                _serialPort.PortName = SelectedPort;
                _serialPort.BaudRate = SelectedBaudRate;
                _serialPort.DataBits = SelectedDataBits;
                _serialPort.Parity = SelectedParity == "ODD" ? Parity.Odd : SelectedParity == "EVEN" ? Parity.Even : Parity.None;
                _serialPort.StopBits = SelectedStopBits == "1.5" ? StopBits.OnePointFive : SelectedStopBits == "2" ? StopBits.Two : StopBits.One;
                _serialPort.RtsEnable = RtsEnabled;
                _serialPort.DtrEnable = DtrEnabled;
                _serialPort.Open();

                IsPortOpen = true;
                StatusText = "串口已打开";
            }
            catch (Exception ex)
            {
                StatusText = "串口操作失败: " + ex.Message;
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                var len = _serialPort.BytesToRead;
                if (len <= 0) return;

                var data = new byte[len];
                _serialPort.Read(data, 0, len);
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ReceiveCount += len;
                    if (IsReceivePaused)
                    {
                        return;
                    }

                    var payload = ReceiveHexMode
                        ? Transform.HexToString(data, " ")
                        : Encoding.GetEncoding("GBK").GetString(data).Replace("\0", "\\0");

                    AppendLine("RX", payload);
                }));
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => StatusText = "接收错误: " + ex.Message));
            }
        }

        private void SendTextData()
        {
            try
            {
                if (!_serialPort.IsOpen)
                {
                    StatusText = "串口未打开";
                    return;
                }

                var input = (SendText ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(input))
                {
                    MessageBox.Show("发送内容不能为空");
                    return;
                }

                var bytes = SendHexMode ? Transform.HexToBytes(input.Replace(" ", string.Empty)) : Encoding.GetEncoding("GBK").GetBytes(input);
                _serialPort.Write(bytes, 0, bytes.Length);

                SendCount += bytes.Length;
                var tx = SendHexMode ? Transform.HexToString(bytes, " ") : Encoding.GetEncoding("GBK").GetString(bytes);
                AppendLine("TX", tx);
                StatusText = "发送成功";
            }
            catch (Exception ex)
            {
                StatusText = "发送失败: " + ex.Message;
            }
        }

        private void ClearReceive()
        {
            ReceiveText = string.Empty;
            ReceiveCount = 0;
        }

        private void ClearCount()
        {
            ReceiveCount = 0;
            SendCount = 0;
        }

        private void SaveReceiveData()
        {
            if (string.IsNullOrWhiteSpace(ReceiveText))
            {
                MessageBox.Show("没有需要保存的内容");
                return;
            }

            if (string.IsNullOrWhiteSpace(SaveDirectory))
            {
                MessageBox.Show("请选择保存路径");
                return;
            }

            try
            {
                Directory.CreateDirectory(SaveDirectory);
                var path = Path.Combine(SaveDirectory, "Data_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".txt");
                File.WriteAllText(path, ReceiveText, Encoding.GetEncoding("GBK"));
                StatusText = "已保存: " + path;
            }
            catch (Exception ex)
            {
                StatusText = "保存失败: " + ex.Message;
            }
        }

        private void ChooseSavePath()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                EnsurePathExists = true,
                Title = "选择保存目录"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                SaveDirectory = dialog.FileName;
            }
        }

        private void OpenFile()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = false,
                EnsureFileExists = true,
                Multiselect = false,
                Title = "选择发送文件"
            };
            dialog.Filters.Add(new CommonFileDialogFilter("文本文件", "*.txt"));
            dialog.Filters.Add(new CommonFileDialogFilter("所有文件", "*.*"));

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            {
                return;
            }

            try
            {
                SendFilePath = dialog.FileName;
                var bytes = File.ReadAllBytes(SendFilePath);
                var preview = bytes.Take(MaxPreviewBytes).ToArray();
                var text = TryDecodePreview(preview);
                AppendLine("FILE", Path.GetFileName(SendFilePath) + "\n" + text);
                StatusText = "文件已加载";
            }
            catch (Exception ex)
            {
                StatusText = "读取文件失败: " + ex.Message;
            }
        }

        private void SendFile()
        {
            try
            {
                if (!_serialPort.IsOpen)
                {
                    MessageBox.Show("串口未打开");
                    return;
                }

                if (string.IsNullOrWhiteSpace(SendFilePath) || !File.Exists(SendFilePath))
                {
                    MessageBox.Show("请先选择有效文件");
                    return;
                }

                var data = File.ReadAllBytes(SendFilePath);
                _serialPort.Write(data, 0, data.Length);
                SendCount += data.Length;
                StatusText = "文件发送成功";
                AppendLine("TX", "已发送文件 " + Path.GetFileName(SendFilePath) + " (" + data.Length + " 字节)");
            }
            catch (Exception ex)
            {
                StatusText = "发送文件失败: " + ex.Message;
            }
        }

        private void AutoSendTimer_Tick(object sender, EventArgs e)
        {
            SendTextData();
        }

        private void AppendLine(string tag, string data)
        {
            var line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [" + tag + "] " + data + Environment.NewLine;

            if (AutoClearEnabled && ReceiveText.Length > ReceiveAutoClearThreshold)
            {
                ReceiveText = string.Empty;
            }

            ReceiveText += line;
        }

        private static string TryDecodePreview(byte[] previewData)
        {
            try
            {
                var utf8 = Encoding.UTF8.GetString(previewData);
                if (utf8.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
                {
                    throw new InvalidDataException();
                }
                return utf8;
            }
            catch
            {
                return Transform.HexToString(previewData, " ");
            }
        }

        public void Dispose()
        {
            _autoSendTimer.Stop();
            _autoSendTimer.Tick -= AutoSendTimer_Tick;
            _serialPort.DataReceived -= SerialPort_DataReceived;

            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }

            _serialPort.Dispose();
        }
    }
}
