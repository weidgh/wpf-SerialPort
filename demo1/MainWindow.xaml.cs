using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace demo1
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private SerialPort _serialPort = new SerialPort();//创建串口对象


        private int receiveCount = 0;//接收字符数量
        private int sendCount = 0;//发送字符数量

        private ConcurrentQueue<(DateTime timestamp, string data)> _dataQueue = new ConcurrentQueue<(DateTime timestamp, string data)>();

        private readonly object _lock = new object();

        private bool _isDisplayPause = false; // 是否暂停接收数据

        private Brush _originalRichTextBackground;

        private DispatcherTimer _autoSendTimer;
        private double _autoSendInterval = 1000; // 默认自动发送间隔为1秒


        public MainWindow()
        {
            InitializeComponent();
            SerialPortLoad();//加载串口

            //注册串口数据接收事件
            _serialPort.DataReceived += SerialPort_DataReceived;

            // 创建新文档并清除默认段落
            receive_richTextBox.Document = new FlowDocument();
            receive_richTextBox.Document.Blocks.Clear();

            // 设置默认背景颜色
            _originalRichTextBackground = receive_richTextBox.Background;

            //初始化计时器
            _autoSendTimer = new DispatcherTimer();
            _autoSendTimer.Tick += AutoSendTimer_Tick;
            _autoSendTimer.Interval = TimeSpan.FromMilliseconds(_autoSendInterval);


            interval_TextBox.Text = "1000";
            interval_TextBox.TextChanged += Interval_TextBox_TextChanged;
            
        }

        private void Interval_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (double.TryParse(interval_TextBox.Text,out double result) && result > 0)
            {
                _autoSendInterval = result;
                _autoSendTimer.Interval = TimeSpan.FromMilliseconds(_autoSendInterval);
            }
            else
            {
                _autoSendInterval = 1000;// 默认值
            }
        }

        private void SerialPortLoad()
        {
            EncodingInfo[] encodingInfos = Encoding.GetEncodings();

            //打开注册表路径
            RegistryKey keyCom = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");

            //获取注册表里所有的值
            string[] valueNames = keyCom.GetValueNames();

            Port_comboBox.Items.Clear();//清空串口列表

            foreach (string valueName in valueNames)
            {
                string portName = (string)keyCom.GetValue(valueName);//获取串口名称
                Port_comboBox.Items.Add(portName);//添加到串口列表
            }

            this.BaudRate_comboBox.SelectedIndex = 0;//波特率默认为9600
            this.Parity_comboBox.SelectedIndex = 0;//校验位默认为无
            this.DataBits_comboBox.SelectedIndex = 0;//数据位默认为8
            this.StopBits_comboBox.SelectedIndex = 0;//停止位默认为1

        }
        private void open_button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 确保 _serialPort 不为 null
                if (_serialPort == null)
                {
                    status_textblock.Text = "串口未初始化";
                    return;
                }

                if (!_serialPort.IsOpen)
                {
                    // 配置串口参数（确保 UI 控件不为空）
                    if (string.IsNullOrEmpty(Port_comboBox.Text))
                    {
                        status_textblock.Text = "未选择串口号";
                        return;
                    }

                    _serialPort.PortName = Port_comboBox.Text;//设置串口名
                    _serialPort.BaudRate = Convert.ToInt32(BaudRate_comboBox.Text);//设置波特率
                    _serialPort.DataBits = Convert.ToInt32(DataBits_comboBox.Text);//设置数据位
                    //
                    switch (Parity_comboBox.SelectedIndex)
                    {
                        //none odd even
                        case 0:
                            _serialPort.Parity = Parity.None;//无
                            break;
                        case 1:
                            _serialPort.Parity = Parity.Odd;//奇
                            break;
                        case 2:
                            _serialPort.Parity = Parity.Even;//偶
                            break;
                        default:
                            break;
                    }
                    //根据下拉框的选择设置停止位
                    switch (StopBits_comboBox.SelectedIndex)
                    {
                        //1 1.5 2
                        case 0:
                            _serialPort.StopBits = StopBits.One;//1
                            break;
                        case 1:
                            _serialPort.StopBits = StopBits.OnePointFive;//1.5
                            break;
                        case 2:
                            _serialPort.StopBits = StopBits.Two;//2
                            break;
                        default:
                            break;
                    }
                    //打开串口
                    _serialPort.Open();

                    open_button.Content = "关闭串口";
                    status_textblock.Text = $"打开{_serialPort.PortName}串口成功";
                }
                else
                {
                    //如果是打开的则关闭串口
                    _serialPort.Close();

                    open_button.Content = "打开串口";
                    status_textblock.Text = $"关闭{_serialPort.PortName}串口成功";
                }
            }
            catch (Exception ex)
            {
                //捕获异常，弹出问题和串口号
                status_textblock.Text = $"打开{_serialPort.PortName}串口异常";
                System.Windows.MessageBox.Show(ex.ToString() + _serialPort.PortName.ToString());
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_serialPort != null)
            {
                _serialPort.DataReceived -= SerialPort_DataReceived; // 注销事件
                if (_serialPort.IsOpen) _serialPort.Close();
                _serialPort.Dispose();
            }
            base.OnClosed(e);
        }

        #region Receive区
        private void autoClear_checkBox_Checked(object sender, RoutedEventArgs e)
        {
            receive_richTextBox_TextChanged(sender, e as TextChangedEventArgs);
        }

        private void receive_richTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (autoClear_checkBox.IsChecked == true && receive_richTextBox != null)
            {
                TextRange _receiveTextRange = new TextRange(
                    receive_richTextBox.Document.ContentStart,
                    receive_richTextBox.Document.ContentEnd
                    );

                if (_receiveTextRange.Text.Length > 1024)
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        receive_richTextBox.Document.Blocks.Clear();
                    }));
                }
            }
        }

        private void clearReceive_button_Click(object sender, RoutedEventArgs e)
        {
            if (receive_richTextBox != null)
            {
                //TextRange textRange = new TextRange(
                //    receive_richTextBox.Document.ContentStart,
                //    receive_richTextBox.Document.ContentEnd
                //    );
                Dispatcher.Invoke(new Action(() =>
                 {
                     receive_richTextBox.Document.Blocks.Clear();
                     receiveCount = 0;
                     receiveCount_textBox.Text = "0";
                 }));
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                lock (_lock)
                {
                    int bytesToRead = _serialPort.BytesToRead;
                    if (bytesToRead <= 0) return;


                    byte[] buffer = new byte[bytesToRead];
                    _serialPort.Read(buffer, 0, bytesToRead);
                    receiveCount += bytesToRead;

                    // 在UI线程获取显示模式
                    bool isHexMode = false;
                    Dispatcher.Invoke(() => isHexMode = receivehex_checkBox.IsChecked == true);

                    // 将数据存入缓冲区（不要直接操作UI控件）
                    string displayData = isHexMode
                        ? Transform.HexToString(buffer, "")
                        : Encoding.GetEncoding("GBK").GetString(buffer).Replace("\0", "\\0");

                    //添加到接收缓冲区
                    _dataQueue.Enqueue((DateTime.Now, displayData));

                    // 自动分割逻辑（示例按时间间隔分割）
                    if (_dataQueue.Count >= 1)//每10条刷新一次
                    {
                        ReceiveFlushBuffer();
                    }
                }

            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                    status_textblock.Text = $"接收错误: {ex.Message}");
            }
        }

        // 定时器触发或手动调用
        private void ReceiveFlushBuffer()
        {
            if (_isDisplayPause)
            {
                receive_richTextBox.Background = Brushes.LightGray;
                receive_richTextBox.Opacity = 0.8;
                return; // 暂停状态不处理
            }

            Dispatcher.Invoke(() =>
            {
                // 恢复正常显示状态
                receive_richTextBox.Background = _originalRichTextBackground;
                receive_richTextBox.Opacity = 1;

                while (_dataQueue.TryDequeue(out var item))
                {
                    var paragrahp = new Paragraph
                    {
                        Margin = new Thickness(0),
                        LineHeight = 12,
                    };

                    paragrahp.Inlines.Add(new Run($"[{item.timestamp:HH:mm:ss.fff}] [RX]"));
                    paragrahp.Inlines.Add(new Run($" {item.data}"));

                    receive_richTextBox.Document.Blocks.Add(paragrahp);
                }

                // 更新接收计数
                receiveCount_textBox.Text = receiveCount.ToString();
                //自动滑动
                receiveCount_textBox.ScrollToEnd();
            });
        }

        private void stop_button_Click(object sender, RoutedEventArgs e)
        {
            _isDisplayPause = !_isDisplayPause;
            // 切换暂停/继续接收状态
            stop_button.Content = _isDisplayPause ? "继续接收" : "暂停接收";
            //更新接收区背景
            Dispatcher.Invoke(() =>
            {
                receive_richTextBox.Background = _isDisplayPause ?
                Brushes.LightGray : _originalRichTextBackground;
            });

            // 非暂停状态强制刷新
            if (!_isDisplayPause) ReceiveFlushBuffer();
        }

        #endregion

        private void AutoSendTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                SerialPort_SendData();
            }
            catch (Exception ex)
            {
                _autoSendTimer.Stop();
                Dispatcher.Invoke(() =>
                {
                    autoSend_checkBox.IsChecked = false;
                    status_textblock.Text = $"自动发送错误: {ex.Message}";
                });
            }
        }

        private void autoSend_checkBox_Checked(object sender, RoutedEventArgs e)
        {
            // 验证输入内容
            TextRange textRange = new TextRange(
                send_richTextBox.Document.ContentStart,
                send_richTextBox.Document.ContentEnd);

            if (string.IsNullOrWhiteSpace(textRange.Text))
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
            SerialPort_SendData();
        }


        private void SerialPort_SendData()
        {
            try
            {
                if (!_serialPort.IsOpen)
                {
                    status_textblock.Text = "串口未打开";
                    return;
                }

                TextRange textRange = new TextRange(send_richTextBox.Document.ContentStart,
                    send_richTextBox.Document.ContentEnd);

                //获取发送数据
                string inputData = textRange.Text.Trim();

                if (string.IsNullOrEmpty(inputData))
                {
                    MessageBox.Show("发送内容不能为空");
                    return;
                }

                // 在UI线程获取显示模式
                bool isHexMode = false;
                Dispatcher.Invoke(() => isHexMode = sendHex_checkBox.IsChecked == true);

                //数据转换
                byte[] sendData;
                try
                {
                    if (isHexMode)
                    {
                        // 清理HEX输入
                        string cleanHex = inputData.Replace(" ", "");

                        if (cleanHex.Length % 2 != 0)
                            throw new FormatException("HEX长度必须为偶数");

                        sendData = Transform.HexToBytes(cleanHex);
                    }
                    else
                    {
                        sendData = Encoding.GetEncoding("GBK").GetBytes(inputData);
                    }
                }
                catch (Exception ex)
                {

                    Dispatcher.Invoke(() =>
                                MessageBox.Show($"数据格式错误: {ex.Message}"));
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    var historyParagraph = new Paragraph
                    {
                        Margin = new Thickness(0),
                        LineHeight = 12,
                    };

                    // 时间戳
                    historyParagraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss.fff}] [TX] ")
                    {
                        Foreground = Brushes.Gray,
                        FontStyle = FontStyles.Italic
                    });

                    // 内容
                    string displayContent = isHexMode
                        ? Transform.HexToString(sendData, " ")
                        : Encoding.GetEncoding("GBK").GetString(sendData);

                    historyParagraph.Inlines.Add(new Run(displayContent));
                    receive_richTextBox.Document.Blocks.Add(historyParagraph);
                    receive_richTextBox.ScrollToEnd();
                });

                //实际发送
                _serialPort.Write(sendData, 0, sendData.Length);
                sendCount += sendData.Length;

                //更新发送计数
                Dispatcher.Invoke(() => sendCount_textBox.Text = sendCount.ToString());
            }
            catch (Exception ex)
            {

                Dispatcher.Invoke(() =>
                    status_textblock.Text = $"发送失败: {ex.Message}");
            }
        }

        private void clearSend_button_Click(object sender, RoutedEventArgs e)
        {
            if (send_richTextBox != null)
            {
                Dispatcher.Invoke(() =>
                {
                    send_richTextBox.Document.Blocks.Clear();
                    sendCount = 0;
                    sendCount_textBox.Text = "0";
                });
            }
        }

        private void saveData_button_Click(object sender, RoutedEventArgs e)
        {

            // 动态获取内容
            var currentTextRange = new TextRange(
                receive_richTextBox.Document.ContentStart,
                receive_richTextBox.Document.ContentEnd
            );

            if (string.IsNullOrEmpty(currentTextRange.Text))
            {
                MessageBox.Show("没有需要保存的内容！");
                return;
            }
            if (string.IsNullOrEmpty(FilePath_textBox.Text))
            {
                MessageBox.Show("请选择保存路径！");
                return;
            }
            // 路径有效性检查
            if (FilePath_textBox.Text.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
            {
                MessageBox.Show("路径包含非法字符");
                return;
            }

            try
            {
                // 创建目录（如果不存在）
                Directory.CreateDirectory(FilePath_textBox.Text);

                // 生成文件名
                string fileName = System.IO.Path.Combine(
                    FilePath_textBox.Text,
                    $"Data_{DateTime.Now:yyyyMMddHHmmss}.txt"
                );

                // 写入文件
                File.WriteAllText(fileName, currentTextRange.Text, Encoding.UTF8);
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
    }
}
