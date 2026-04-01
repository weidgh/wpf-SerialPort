using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace demo1
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly SerialPort _serialPort = new SerialPort();
        private readonly object _serialReadLock = new object();
        private readonly ConcurrentQueue<ReceiveFrame> _receiveQueue = new ConcurrentQueue<ReceiveFrame>();
        private readonly DispatcherTimer _autoSendTimer;

        private Brush _originalReceiveBackground;
        private bool _isReceivePaused;
        private int _receiveCount;
        private int _sendCount;

        private const int MaxPreviewBytes = 4000;
        private const int DefaultAutoSendInterval = 1000;

        public MainWindow()
        {
            InitializeComponent();

            _serialPort.DataReceived += SerialPort_DataReceived;

            receive_richTextBox.Document = new FlowDocument();
            receive_richTextBox.Document.Blocks.Clear();
            _originalReceiveBackground = receive_richTextBox.Background;

            _autoSendTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DefaultAutoSendInterval)
            };
            _autoSendTimer.Tick += AutoSendTimer_Tick;

            interval_TextBox.Text = DefaultAutoSendInterval.ToString();
            interval_TextBox.TextChanged += Interval_TextBox_TextChanged;

            ResetCounters();
            LoadSerialPorts();
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoSendTimer.Stop();
            _autoSendTimer.Tick -= AutoSendTimer_Tick;

            _serialPort.DataReceived -= SerialPort_DataReceived;
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            _serialPort.Dispose();

            base.OnClosed(e);
        }

        private void LoadSerialPorts()
        {
            Port_comboBox.Items.Clear();

            var ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                using (var keyCom = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
                {
                    var valueNames = keyCom?.GetValueNames() ?? Array.Empty<string>();
                    foreach (var valueName in valueNames)
                    {
                        if (keyCom?.GetValue(valueName) is string name)
                        {
                            Port_comboBox.Items.Add(name);
                        }
                    }
                }
            }
            else
            {
                foreach (var port in ports.OrderBy(p => p))
                {
                    Port_comboBox.Items.Add(port);
                }
            }

            if (Port_comboBox.Items.Count > 0)
            {
                Port_comboBox.SelectedIndex = 0;
            }

            BaudRate_comboBox.SelectedIndex = 0;
            Parity_comboBox.SelectedIndex = 0;
            DataBits_comboBox.SelectedIndex = 0;
            StopBits_comboBox.SelectedIndex = 0;
        }

        private void Interval_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!double.TryParse(interval_TextBox.Text, out var intervalMs) || intervalMs <= 0)
            {
                intervalMs = DefaultAutoSendInterval;
            }

            _autoSendTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        }

        private void open_button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                    open_button.Content = "打开串口";
                    status_textblock.Text = $"关闭{_serialPort.PortName}串口成功";
                    return;
                }

                if (string.IsNullOrWhiteSpace(Port_comboBox.Text))
                {
                    status_textblock.Text = "未选择串口号";
                    return;
                }

                _serialPort.PortName = Port_comboBox.Text;
                _serialPort.BaudRate = Convert.ToInt32(BaudRate_comboBox.Text);
                _serialPort.DataBits = Convert.ToInt32(DataBits_comboBox.Text);
                _serialPort.Parity = ResolveParity();
                _serialPort.StopBits = ResolveStopBits();
                _serialPort.RtsEnable = RTS_checkBox.IsChecked == true;
                _serialPort.DtrEnable = DTR_checkBox.IsChecked == true;

                _serialPort.Open();

                open_button.Content = "关闭串口";
                status_textblock.Text = $"打开{_serialPort.PortName}串口成功";
            }
            catch (Exception ex)
            {
                status_textblock.Text = $"打开串口异常: {ex.Message}";
                MessageBox.Show(ex.ToString());
            }
        }

        private Parity ResolveParity()
        {
            switch (Parity_comboBox.SelectedIndex)
            {
                case 1:
                    return Parity.Odd;
                case 2:
                    return Parity.Even;
                default:
                    return Parity.None;
            }
        }

        private StopBits ResolveStopBits()
        {
            switch (StopBits_comboBox.SelectedIndex)
            {
                case 1:
                    return StopBits.OnePointFive;
                case 2:
                    return StopBits.Two;
                default:
                    return StopBits.One;
            }
        }

        #region Receive

        private void autoClear_checkBox_Checked(object sender, RoutedEventArgs e)
        {
            AutoClearReceiveIfNeeded();
        }

        private void receive_richTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            AutoClearReceiveIfNeeded();
        }

        private void AutoClearReceiveIfNeeded()
        {
            if (autoClear_checkBox.IsChecked != true)
            {
                return;
            }

            var text = new TextRange(receive_richTextBox.Document.ContentStart, receive_richTextBox.Document.ContentEnd).Text;
            if (text.Length <= 1024)
            {
                return;
            }

            receive_richTextBox.Document.Blocks.Clear();
        }

        private void clearReceive_button_Click(object sender, RoutedEventArgs e)
        {
            receive_richTextBox.Document.Blocks.Clear();
            _receiveCount = 0;
            receiveCount_textBox.Text = "0";
        }

        private void stop_button_Click(object sender, RoutedEventArgs e)
        {
            _isReceivePaused = !_isReceivePaused;
            stop_button.Content = _isReceivePaused ? "继续接收" : "暂停接收";
            receive_richTextBox.Background = _isReceivePaused ? Brushes.LightGray : _originalReceiveBackground;
            receive_richTextBox.Opacity = _isReceivePaused ? 0.8 : 1;

            if (!_isReceivePaused)
            {
                FlushReceiveQueue();
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                lock (_serialReadLock)
                {
                    var bytesToRead = _serialPort.BytesToRead;
                    if (bytesToRead <= 0)
                    {
                        return;
                    }

                    var buffer = new byte[bytesToRead];
                    _serialPort.Read(buffer, 0, bytesToRead);
                    _receiveCount += bytesToRead;

                    var isHexMode = Dispatcher.Invoke(() => receivehex_checkBox.IsChecked == true);
                    var display = isHexMode
                        ? Transform.HexToString(buffer)
                        : Encoding.GetEncoding("GBK").GetString(buffer).Replace("\0", "\\0");

                    _receiveQueue.Enqueue(new ReceiveFrame(DateTime.Now, display));
                }

                FlushReceiveQueue();
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => status_textblock.Text = $"接收错误: {ex.Message}");
            }
        }

        private void FlushReceiveQueue()
        {
            if (_isReceivePaused)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                while (_receiveQueue.TryDequeue(out var frame))
                {
                    AppendReceiveParagraph(frame.Timestamp, "RX", frame.DisplayText, Brushes.Black, false);
                }

                receiveCount_textBox.Text = _receiveCount.ToString();
                receive_richTextBox.ScrollToEnd();
            });
        }

        private void AppendReceiveParagraph(DateTime timestamp, string tag, string content, Brush contentColor, bool italicHeader)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0),
                LineHeight = 12
            };

            paragraph.Inlines.Add(new Run($"[{timestamp:HH:mm:ss.fff}] [{tag}] ")
            {
                Foreground = Brushes.Gray,
                FontStyle = italicHeader ? FontStyles.Italic : FontStyles.Normal
            });
            paragraph.Inlines.Add(new Run(content)
            {
                Foreground = contentColor
            });

            receive_richTextBox.Document.Blocks.Add(paragraph);
        }

        #endregion

        #region Send

        private void AutoSendTimer_Tick(object sender, EventArgs e)
        {
            SendCurrentInput();
        }

        private void autoSend_checkBox_Checked(object sender, RoutedEventArgs e)
        {
            var text = new TextRange(send_richTextBox.Document.ContentStart, send_richTextBox.Document.ContentEnd).Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("自动发送内容不能为空");
                autoSend_checkBox.IsChecked = false;
                return;
            }

            _autoSendTimer.Start();
        }

        private void autoSend_checkBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _autoSendTimer.Stop();
        }

        private void send_button_Click(object sender, RoutedEventArgs e)
        {
            SendCurrentInput();
        }

        private void SendCurrentInput()
        {
            try
            {
                if (!_serialPort.IsOpen)
                {
                    status_textblock.Text = "串口未打开";
                    return;
                }

                var textRange = new TextRange(send_richTextBox.Document.ContentStart, send_richTextBox.Document.ContentEnd);
                var input = textRange.Text.Trim();
                if (string.IsNullOrWhiteSpace(input))
                {
                    MessageBox.Show("发送内容不能为空");
                    return;
                }

                var isHexMode = sendHex_checkBox.IsChecked == true;
                var payload = isHexMode ? Transform.HexToBytes(input.Replace(" ", string.Empty)) : Encoding.GetEncoding("GBK").GetBytes(input);

                _serialPort.Write(payload, 0, payload.Length);
                _sendCount += payload.Length;
                sendCount_textBox.Text = _sendCount.ToString();

                var display = isHexMode ? Transform.HexToString(payload, " ") : Encoding.GetEncoding("GBK").GetString(payload);
                AppendReceiveParagraph(DateTime.Now, "TX", display, Brushes.Black, true);
                receive_richTextBox.ScrollToEnd();
            }
            catch (FormatException ex)
            {
                MessageBox.Show($"数据格式错误: {ex.Message}");
            }
            catch (Exception ex)
            {
                status_textblock.Text = $"发送失败: {ex.Message}";
            }
        }

        private void clearSend_button_Click(object sender, RoutedEventArgs e)
        {
            send_richTextBox.Document.Blocks.Clear();
            _sendCount = 0;
            sendCount_textBox.Text = "0";
        }

        #endregion

        private void saveData_button_Click(object sender, RoutedEventArgs e)
        {
            var content = new TextRange(receive_richTextBox.Document.ContentStart, receive_richTextBox.Document.ContentEnd).Text;
            if (string.IsNullOrWhiteSpace(content))
            {
                MessageBox.Show("没有需要保存的内容！");
                return;
            }

            var dir = FilePath_textBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(dir))
            {
                MessageBox.Show("请选择保存路径！");
                return;
            }

            if (dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                MessageBox.Show("路径包含非法字符");
                return;
            }

            try
            {
                Directory.CreateDirectory(dir);
                var fileName = Path.Combine(dir, $"Data_{DateTime.Now:yyyyMMddHHmmss}.txt");
                File.WriteAllText(fileName, content, Encoding.GetEncoding("GBK"));
                MessageBox.Show($"文件已保存到：{fileName}");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                MessageBox.Show($"保存失败：{ex.Message}");
            }
        }

        private void savePath_button_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog
            {
                Title = "选择保存路径",
                IsFolderPicker = true,
                InitialDirectory = FilePath_textBox.Text,
                EnsurePathExists = true
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                FilePath_textBox.Text = dialog.FileName;
            }
        }

        private void openFile_button_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog
            {
                Title = "请选择文件",
                IsFolderPicker = false,
                EnsureFileExists = true,
                Multiselect = false,
                InitialDirectory = sendfile_textbox.Text
            };
            dialog.Filters.Add(new CommonFileDialogFilter("文本文件", "*.txt"));

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            {
                return;
            }

            try
            {
                var filePath = dialog.FileName;
                sendfile_textbox.Text = filePath;

                var fileInfo = new FileInfo(filePath);
                var fileSize = fileInfo.Length;
                var previewLength = (int)Math.Min(MaxPreviewBytes, fileSize);

                byte[] previewData;
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    previewData = new byte[previewLength];
                    fs.Read(previewData, 0, previewLength);
                }

                string previewText;
                try
                {
                    previewText = Encoding.UTF8.GetString(previewData);
                    if (previewText.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
                    {
                        throw new InvalidDataException("包含控制字符");
                    }
                }
                catch
                {
                    previewText = BitConverter.ToString(previewData).Replace("-", " ");
                }

                var message = $"{Path.GetFileName(filePath)} ({fileSize}字节){Environment.NewLine}[预览前{previewLength}字节]:{Environment.NewLine}{previewText}";
                AppendReceiveParagraph(DateTime.Now, "已加载", message, Brushes.DarkBlue, false);
                receive_richTextBox.ScrollToEnd();
            }
            catch (Exception)
            {
                MessageBox.Show("文件读取失败");
            }
        }

        private void sendFile_button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_serialPort.IsOpen)
                {
                    MessageBox.Show("串口未打开");
                    return;
                }

                var filePath = sendfile_textbox.Text.Trim();
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    MessageBox.Show("请先选择文件");
                    return;
                }

                if (!File.Exists(filePath))
                {
                    MessageBox.Show("文件不存在");
                    return;
                }

                var fileData = File.ReadAllBytes(filePath);
                _serialPort.Write(fileData, 0, fileData.Length);

                _sendCount += fileData.Length;
                sendCount_textBox.Text = _sendCount.ToString();
                status_textblock.Text = $"发送文件成功: {Path.GetFileName(filePath)} ({fileData.Length}字节)";
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("文件访问被拒绝");
            }
            catch (IOException ex)
            {
                MessageBox.Show($"文件读取失败：{ex.Message}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"发送失败：{ex.Message}");
            }
        }

        private void clearCount_button_Click(object sender, RoutedEventArgs e)
        {
            ResetCounters();
        }

        private void ResetCounters()
        {
            _sendCount = 0;
            _receiveCount = 0;
            sendCount_textBox.Text = "0";
            receiveCount_textBox.Text = "0";
        }

        private void RTS_checkBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.RtsEnable = RTS_checkBox.IsChecked == true;
            }
        }

        private void DTR_checkBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.DtrEnable = DTR_checkBox.IsChecked == true;
            }
        }
    }
}
