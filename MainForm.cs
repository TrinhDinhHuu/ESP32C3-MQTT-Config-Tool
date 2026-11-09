using System.IO.Ports;
using System.Text;
using ClosedXML.Excel;

namespace ESP32C3_ConfigTool;

public sealed class MainForm : Form
{
    private const byte H = 0xAA, T = 0x55;
    private const ushort SetUser = 0x0001, SetPass = 0x0002, Save = 0x0003, SetUri = 0x0004;

    private readonly ComboBox portBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly ComboBox baudBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly Button refreshButton = new() { Text = "Làm mới COM", AutoSize = true };
    private readonly Button connectButton = new() { Text = "Kết nối UART1", AutoSize = true };
    private readonly Button disconnectButton = new() { Text = "Ngắt kết nối", AutoSize = true, Enabled = false };
    private readonly TextBox uriBox = new() { Width = 590, Text = "mqtts://befb85156c3248e0b71cfd0d1ee6da3b.s1.eu.hivemq.cloud:8883" };
    private readonly TextBox userBox = new() { Width = 260, Text = "Paul.Dinh" };
    private readonly TextBox passBox = new() { Width = 260, UseSystemPasswordChar = true };
    private readonly CheckBox showPass = new() { Text = "Hiện mật khẩu", AutoSize = true };
    private readonly TextBox summaryBox = new() { Multiline = true, ReadOnly = true, Width = 590, Height = 76, ScrollBars = ScrollBars.Vertical };
    private readonly Button copyButton = new() { Text = "Copy toàn bộ cấu hình", AutoSize = true };
    private readonly Button saveTxtButton = new() { Text = "Lưu TXT", AutoSize = true };
    private readonly Button openTxtButton = new() { Text = "Mở TXT", AutoSize = true };
    private readonly Button saveExcelButton = new() { Text = "Lưu Excel", AutoSize = true };
    private readonly Button openExcelButton = new() { Text = "Mở Excel", AutoSize = true };
    private readonly Button setupButton = new() { Text = "THIẾT LẬP ESP32-C3", AutoSize = true, Height = 38, Enabled = false };
    private readonly Label userResult = ResultLabel("Chưa gửi");
    private readonly Label passResult = ResultLabel("Chưa gửi");
    private readonly Label uriResult = ResultLabel("Chưa gửi");
    private readonly Label saveResult = ResultLabel("Chưa gửi");
    private readonly Label finalResult = new() { Text = "KẾT QUẢ: CHƯA THỰC HIỆN", AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Color.DimGray };
    private readonly Button clearButton = new() { Text = "Xóa log", AutoSize = true };
    private readonly RichTextBox logBox = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(18,25,38), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 9.5f) };
    private readonly Label statusLabel = new() { Text = "Chưa kết nối", AutoSize = true, ForeColor = Color.DarkRed };

    private SerialPort? serial;
    private readonly object rxLock = new();
    private readonly List<byte> rx = new();
    private TaskCompletionSource<byte>? pendingAck;
    private ushort expectedAck;
    private bool sending;

    public MainForm()
    {
        Text = "ESP32-C3 MQTT Configuration Tool";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1020, 850);
        MinimumSize = new Size(880, 700);
        Font = new Font("Segoe UI", 9.5f);
        baudBox.Items.AddRange(new object[] { "115200", "9600", "57600", "230400" });
        baudBox.SelectedItem = "115200";
        BuildUi();
        RefreshSummary();
        RefreshPorts();

        refreshButton.Click += (_, _) => RefreshPorts();
        connectButton.Click += (_, _) => ConnectPort();
        disconnectButton.Click += (_, _) => DisconnectPort();
        setupButton.Click += async (_, _) => await SetupAsync();
        copyButton.Click += (_, _) => CopyConfiguration();
        saveTxtButton.Click += (_, _) => SaveTxtConfiguration();
        openTxtButton.Click += (_, _) => OpenTxtConfiguration();
        saveExcelButton.Click += (_, _) => SaveExcelConfiguration();
        openExcelButton.Click += (_, _) => OpenExcelConfiguration();
        clearButton.Click += (_, _) => logBox.Clear();
        showPass.CheckedChanged += (_, _) => passBox.UseSystemPasswordChar = !showPass.Checked;
        uriBox.TextChanged += (_, _) => RefreshSummary();
        userBox.TextChanged += (_, _) => RefreshSummary();
        passBox.TextChanged += (_, _) => RefreshSummary();
        FormClosing += (_, _) => DisconnectPort();
    }

    private static Label ResultLabel(string text) =>
        new() { Text = "○ " + text, AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(3, 5, 12, 5) };

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var serialGroup = new GroupBox { Text = "1. Kết nối UART1", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        var serialFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        serialFlow.Controls.AddRange(new Control[] { L("COM:"), portBox, refreshButton, L("Baud:"), baudBox, connectButton, disconnectButton, statusLabel });
        serialGroup.Controls.Add(serialFlow);
        root.Controls.Add(serialGroup);

        var cfgGroup = new GroupBox { Text = "2. Thông tin cấu hình MQTT", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(grid, "Broker URI:", uriBox);
        AddRow(grid, "Username:", userBox);
        var passPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        passPanel.Controls.Add(passBox);
        passPanel.Controls.Add(showPass);
        AddRow(grid, "Password:", passPanel);
        AddRow(grid, "Thông tin copy:", summaryBox);
        var copyPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        copyPanel.Controls.AddRange(new Control[] { copyButton, saveTxtButton, openTxtButton, saveExcelButton, openExcelButton });
        AddRow(grid, "", copyPanel);
        cfgGroup.Controls.Add(grid);
        root.Controls.Add(cfgGroup);

        var resultGroup = new GroupBox { Text = "3. Thiết lập và kết quả", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        var resultFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        resultFlow.Controls.Add(setupButton);
        resultFlow.Controls.Add(ResultRow("Username", userResult));
        resultFlow.Controls.Add(ResultRow("Password", passResult));
        resultFlow.Controls.Add(ResultRow("Broker URI", uriResult));
        resultFlow.Controls.Add(ResultRow("SAVE vào NVS", saveResult));
        resultFlow.Controls.Add(finalResult);
        resultGroup.Controls.Add(resultFlow);
        root.Controls.Add(resultGroup);

        var logHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 7, 0, 4) };
        logHeader.Controls.Add(new Label { Text = "4. UART Log (HEX)", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 5, 12, 0) });
        logHeader.Controls.Add(clearButton);
        root.Controls.Add(logHeader);
        root.Controls.Add(logBox);
    }

    private static Control ResultRow(string name, Label result)
    {
        var p = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        p.Controls.Add(new Label { Text = name + ":", Width = 125, AutoSize = false, Padding = new Padding(3, 5, 0, 0) });
        p.Controls.Add(result);
        return p;
    }

    private static Label L(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(0, 7, 5, 0) };

    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(3, 5, 3, 5);
        grid.Controls.Add(L(label), 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private void RefreshSummary()
    {
        summaryBox.Text =
            $"Broker URI: {uriBox.Text.Trim()}{Environment.NewLine}" +
            $"Username: {userBox.Text}{Environment.NewLine}" +
            $"Password: {passBox.Text}";
    }

    private void CopyConfiguration()
    {
        RefreshSummary();
        try
        {
            Clipboard.SetText(summaryBox.Text);
            Log("INFO", "Đã copy Broker URI, Username và Password vào clipboard.");
        }
        catch (Exception ex)
        {
            Log("ERROR", "Không copy được clipboard: " + ex.Message);
            MessageBox.Show("Không thể copy cấu hình: " + ex.Message);
        }
    }


    // Lấy cấu hình hiện tại từ các ô nhập liệu.
    private (string Uri, string Username, string Password) GetConfiguration()
        => (uriBox.Text.Trim(), userBox.Text, passBox.Text);

    // Kiểm tra cấu hình trước khi lưu hoặc sử dụng.
    private bool ValidateConfigurationForFile(string uri, string username, string password)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
            !(parsed.Scheme == "mqtt" || parsed.Scheme == "mqtts") ||
            string.IsNullOrWhiteSpace(parsed.Host))
        {
            MessageBox.Show("Broker URI không hợp lệ. Ví dụ: mqtts://hostname:8883",
                "Cấu hình không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            MessageBox.Show("Username và Password không được để trống.",
                "Cấu hình không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    // Chỉ cập nhật giao diện sau khi đọc và kiểm tra file thành công.
    private void ApplyConfiguration(string uri, string username, string password)
    {
        uriBox.Text = uri;
        userBox.Text = username;
        passBox.Text = password;
        RefreshSummary();
        ResetResults();
        Log("SUCCESS", "Đã nạp cấu hình vào giao diện.");
    }

    private void SaveTxtConfiguration()
    {
        var cfg = GetConfiguration();
        if (!ValidateConfigurationForFile(cfg.Uri, cfg.Username, cfg.Password)) return;

        using var dialog = new SaveFileDialog
        {
            Title = "Lưu cấu hình MQTT dạng TXT",
            Filter = "Text file (*.txt)|*.txt",
            DefaultExt = "txt",
            AddExtension = true,
            FileName = "esp32c3_mqtt_config.txt"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            // UTF-8; lưu dạng key=value để người dùng đọc trực tiếp bằng Notepad.
            // Không dùng ký tự xuống dòng trong các trường cấu hình.
            if (cfg.Uri.Contains('\n') || cfg.Username.Contains('\n') || cfg.Password.Contains('\n') ||
                cfg.Uri.Contains('\r') || cfg.Username.Contains('\r') || cfg.Password.Contains('\r'))
                throw new InvalidDataException("Các trường cấu hình không được chứa ký tự xuống dòng.");

            string content =
                "# ESP32-C3 MQTT configuration" + Environment.NewLine +
                "BrokerURI=" + cfg.Uri + Environment.NewLine +
                "Username=" + cfg.Username + Environment.NewLine +
                "Password=" + cfg.Password + Environment.NewLine;
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(false));
            Log("SUCCESS", "Đã lưu file TXT: " + dialog.FileName);
            MessageBox.Show("Đã lưu cấu hình TXT thành công.", "Lưu TXT",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("ERROR", "Lưu TXT thất bại: " + ex.Message);
            MessageBox.Show("Không lưu được TXT: " + ex.Message);
        }
    }

    private void OpenTxtConfiguration()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Mở cấu hình MQTT dạng TXT",
            Filter = "Text file (*.txt)|*.txt",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(dialog.FileName, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                int separator = line.IndexOf('=');
                if (separator <= 0) continue;
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            string ReadText(string key)
            {
                if (!values.TryGetValue(key, out string? value))
                    throw new InvalidDataException("Thiếu trường " + key + " trong file.");
                return value;
            }

            string uri = ReadText("BrokerURI");
            string username = ReadText("Username");
            string password = ReadText("Password");
            if (!ValidateConfigurationForFile(uri, username, password)) return;
            ApplyConfiguration(uri, username, password);
            Log("INFO", "Đã mở file TXT: " + dialog.FileName);
        }
        catch (Exception ex)
        {
            Log("ERROR", "Mở TXT thất bại: " + ex.Message);
            MessageBox.Show("Không đọc được file TXT: " + ex.Message);
        }
    }

    private void SaveExcelConfiguration()
    {
        var cfg = GetConfiguration();
        if (!ValidateConfigurationForFile(cfg.Uri, cfg.Username, cfg.Password)) return;

        using var dialog = new SaveFileDialog
        {
            Title = "Lưu cấu hình MQTT dạng Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true,
            FileName = "esp32c3_mqtt_config.xlsx"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("MQTT Config");
            sheet.Cell(1, 1).Value = "ESP32-C3 MQTT Configuration";
            sheet.Range(1, 1, 1, 2).Merge();
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 14;

            sheet.Cell(3, 1).Value = "Broker URI";
            sheet.Cell(3, 2).Value = cfg.Uri;
            sheet.Cell(4, 1).Value = "Username";
            sheet.Cell(4, 2).Value = cfg.Username;
            sheet.Cell(5, 1).Value = "Password";
            sheet.Cell(5, 2).Value = cfg.Password;

            sheet.Range(3, 1, 5, 1).Style.Font.Bold = true;
            sheet.Columns().AdjustToContents();
            sheet.Column(2).Width = Math.Min(Math.Max(sheet.Column(2).Width, 28), 70);
            sheet.SheetView.FreezeRows(2);
            workbook.SaveAs(dialog.FileName);

            Log("SUCCESS", "Đã lưu file Excel: " + dialog.FileName);
            MessageBox.Show("Đã lưu cấu hình Excel thành công.", "Lưu Excel",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("ERROR", "Lưu Excel thất bại: " + ex.Message);
            MessageBox.Show("Không lưu được Excel: " + ex.Message);
        }
    }

    private void OpenExcelConfiguration()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Mở cấu hình MQTT từ Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var workbook = new XLWorkbook(dialog.FileName);
            var sheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name == "MQTT Config")
                ?? workbook.Worksheets.First();

            string ReadValue(string key)
            {
                int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
                for (int row = 1; row <= lastRow; row++)
                {
                    if (string.Equals(sheet.Cell(row, 1).GetString().Trim(), key,
                        StringComparison.OrdinalIgnoreCase))
                        return sheet.Cell(row, 2).GetString();
                }
                throw new InvalidDataException("Không tìm thấy trường " + key + " trong file Excel.");
            }

            string uri = ReadValue("Broker URI");
            string username = ReadValue("Username");
            string password = ReadValue("Password");
            if (!ValidateConfigurationForFile(uri, username, password)) return;
            ApplyConfiguration(uri, username, password);
            Log("INFO", "Đã mở file Excel: " + dialog.FileName);
        }
        catch (Exception ex)
        {
            Log("ERROR", "Mở Excel thất bại: " + ex.Message);
            MessageBox.Show("Không đọc được file Excel: " + ex.Message);
        }
    }

    private void ResetResults()
    {
        SetResult(userResult, "Chưa gửi", null);
        SetResult(passResult, "Chưa gửi", null);
        SetResult(uriResult, "Chưa gửi", null);
        SetResult(saveResult, "Chưa gửi", null);
        finalResult.Text = "KẾT QUẢ: ĐANG THỰC HIỆN";
        finalResult.ForeColor = Color.DarkOrange;
    }

    private static void SetResult(Label label, string text, bool? ok)
    {
        label.Text = (ok == true ? "✓ " : ok == false ? "✗ " : "○ ") + text;
        label.ForeColor = ok == true ? Color.ForestGreen : ok == false ? Color.Firebrick : Color.DimGray;
    }

    private void RefreshPorts()
    {
        string? old = portBox.SelectedItem?.ToString();
        portBox.Items.Clear();
        var names = SerialPort.GetPortNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        portBox.Items.AddRange(names);
        if (old != null && names.Contains(old)) portBox.SelectedItem = old;
        else if (names.Length > 0) portBox.SelectedIndex = 0;
        Log("INFO", names.Length == 0 ? "Không tìm thấy COM." : $"COM: {string.Join(", ", names)}");
    }

    private void ConnectPort()
    {
        if (portBox.SelectedItem == null) { MessageBox.Show("Chọn cổng COM trước."); return; }
        try
        {
            serial = new SerialPort(portBox.SelectedItem.ToString()!, int.Parse(baudBox.Text), Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None, ReadTimeout = 500, WriteTimeout = 2000,
                DtrEnable = false, RtsEnable = false
            };
            serial.DataReceived += OnDataReceived;
            serial.Open();
            connectButton.Enabled = false;
            disconnectButton.Enabled = setupButton.Enabled = true;
            portBox.Enabled = baudBox.Enabled = refreshButton.Enabled = false;
            statusLabel.Text = $"Đã kết nối {serial.PortName} @ {serial.BaudRate}";
            statusLabel.ForeColor = Color.DarkGreen;
            Log("INFO", $"Open {serial.PortName}, {serial.BaudRate}, 8-N-1, no flow control.");
        }
        catch (Exception ex)
        {
            Log("ERROR", ex.Message);
            MessageBox.Show(ex.Message, "Lỗi mở COM");
            DisconnectPort();
        }
    }

    private void DisconnectPort()
    {
        try
        {
            if (serial != null)
            {
                serial.DataReceived -= OnDataReceived;
                if (serial.IsOpen) serial.Close();
                serial.Dispose();
                serial = null;
            }
        }
        catch (Exception ex) { Log("ERROR", ex.Message); }

        lock (rxLock)
        {
            rx.Clear();
            pendingAck?.TrySetException(new IOException("UART disconnected."));
            pendingAck = null;
        }
        if (!IsDisposed)
        {
            connectButton.Enabled = true;
            disconnectButton.Enabled = setupButton.Enabled = false;
            portBox.Enabled = baudBox.Enabled = refreshButton.Enabled = true;
            statusLabel.Text = "Chưa kết nối";
            statusLabel.ForeColor = Color.DarkRed;
        }
    }

    private void OnDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var p = serial;
            if (p == null || !p.IsOpen) return;
            int count = p.BytesToRead;
            if (count <= 0) return;
            byte[] bytes = new byte[count];
            int n = p.Read(bytes, 0, count);
            if (n <= 0) return;
            LogHex("RX", bytes.AsSpan(0, n).ToArray());
            lock (rxLock)
            {
                for (int i = 0; i < n; i++) rx.Add(bytes[i]);
                ParseFramesLocked();
            }
        }
        catch (Exception ex) { Log("ERROR", $"RX: {ex.Message}"); }
    }

    private void ParseFramesLocked()
    {
        while (true)
        {
            int start = -1;
            for (int i = 0; i + 1 < rx.Count; i++)
                if (rx[i] == H && rx[i + 1] == H) { start = i; break; }

            if (start < 0)
            {
                bool keep = rx.Count > 0 && rx[^1] == H;
                rx.Clear();
                if (keep) rx.Add(H);
                return;
            }
            if (start > 0) rx.RemoveRange(0, start);
            if (rx.Count < 5) return;

            int len = rx[4];
            if (len < 4) { rx.RemoveAt(0); continue; }
            int total = len + 4;
            if (total > 260) { rx.RemoveAt(0); continue; }
            if (rx.Count < total) return;
            if (rx[total - 2] != T || rx[total - 1] != T) { rx.RemoveAt(0); continue; }

            byte[] frame = rx.Take(total).ToArray();
            rx.RemoveRange(0, total);
            byte cs = Xor(frame, 2, len - 1);
            if (cs != frame[total - 3]) { Log("WARN", $"Checksum sai RX={frame[total-3]:X2}, CALC={cs:X2}"); continue; }

            ushort cmd = (ushort)((frame[2] << 8) | frame[3]);
            int dataLen = len - 4;
            byte status = dataLen > 0 ? frame[5] : (byte)0xFF;
            Log("INFO", $"Frame hợp lệ CMD=0x{cmd:X4}, STATUS=0x{status:X2}");
            if (pendingAck != null && cmd == expectedAck && dataLen >= 1)
                pendingAck.TrySetResult(status);
        }
    }

    private static byte Xor(byte[] bytes, int offset, int count)
    {
        byte x = 0;
        for (int i = 0; i < count; i++) x ^= bytes[offset + i];
        return x;
    }

    private async Task SetupAsync()
    {
        if (sending || serial?.IsOpen != true) return;
        string uri = uriBox.Text.Trim(), username = userBox.Text, password = passBox.Text;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
            !(parsed.Scheme == "mqtt" || parsed.Scheme == "mqtts") || string.IsNullOrWhiteSpace(parsed.Host))
        {
            MessageBox.Show("URI cần dạng mqtts://hostname:8883 hoặc mqtt://hostname:1883.");
            return;
        }
        if (username.Length == 0 || password.Length == 0) { MessageBox.Show("Username và password không được trống."); return; }

        byte[] u = Encoding.UTF8.GetBytes(username), p = Encoding.UTF8.GetBytes(password), b = Encoding.UTF8.GetBytes(uri);
        if (u.Length > 64 || p.Length > 128 || b.Length > 248)
        {
            MessageBox.Show("Vượt giới hạn firmware: username 64, password 128, broker URI 248 byte.");
            return;
        }

        sending = true;
        setupButton.Enabled = false;
        ResetResults();
        bool allOk = false;
        Label currentStep = userResult;
        string currentStepName = "Username";
        try
        {
            await SendAndWaitAsync(SetUser, u, 0x0081);
            SetResult(userResult, "OK", true);

            currentStep = passResult;
            currentStepName = "Password";
            await SendAndWaitAsync(SetPass, p, 0x0082);
            SetResult(passResult, "OK", true);

            currentStep = uriResult;
            currentStepName = "Broker URI";
            await SendAndWaitAsync(SetUri, b, 0x0084);
            SetResult(uriResult, "OK", true);

            currentStep = saveResult;
            currentStepName = "SAVE NVS";
            await SendAndWaitAsync(Save, Array.Empty<byte>(), 0x0083);
            SetResult(saveResult, "OK", true);
            allOk = true;
            finalResult.Text = "KẾT QUẢ: OK";
            finalResult.ForeColor = Color.ForestGreen;
            Log("SUCCESS", "Tất cả mục đều OK; ESP32 đã ACK SAVE thành công.");
            MessageBox.Show("Thiết lập thành công!\nUsername: OK\nPassword: OK\nBroker URI: OK\nSAVE NVS: OK",
                "KẾT QUẢ: OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetResult(currentStep, "NG - " + currentStepName, false);
            finalResult.Text = "KẾT QUẢ: NG";
            finalResult.ForeColor = Color.Firebrick;
            Log("ERROR", ex.Message);
            MessageBox.Show("Thiết lập chưa hoàn tất.\nKết quả: NG\n" + ex.Message,
                "KẾT QUẢ: NG", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            sending = false;
            setupButton.Enabled = serial?.IsOpen == true;
            if (!allOk) Log("WARN", "Quy trình cấu hình kết thúc với kết quả NG.");
        }
    }

    private async Task SendAndWaitAsync(ushort cmd, byte[] data, ushort ack)
    {
        byte[] frame = BuildFrame(cmd, data);
        var tcs = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (rxLock) { expectedAck = ack; pendingAck = tcs; }
        try
        {
            var p = serial ?? throw new IOException("COM chưa mở.");
            if (!p.IsOpen) throw new IOException("COM đã đóng.");
            p.Write(frame, 0, frame.Length);
            LogHex("TX", frame);
            Log("INFO", $"Gửi CMD 0x{cmd:X4}; chờ ACK 0x{ack:X4}.");
            byte status;
            try { status = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
            catch (TimeoutException) { throw new TimeoutException($"Không nhận ACK 0x{ack:X4} trong 4 giây. Dừng quy trình."); }
            if (status != 0) throw new IOException($"ACK 0x{ack:X4} có STATUS=0x{status:X2}. Dừng quy trình.");
            Log("SUCCESS", $"ACK 0x{ack:X4}, STATUS=0x00.");
        }
        finally
        {
            lock (rxLock) if (ReferenceEquals(pendingAck, tcs)) pendingAck = null;
        }
    }

    private static byte[] BuildFrame(ushort cmd, byte[] data)
    {
        if (data.Length > 248) throw new ArgumentOutOfRangeException(nameof(data));
        byte len = checked((byte)(data.Length + 4));
        byte[] f = new byte[data.Length + 8];
        f[0] = H; f[1] = H; f[2] = (byte)(cmd >> 8); f[3] = (byte)cmd; f[4] = len;
        Buffer.BlockCopy(data, 0, f, 5, data.Length);
        f[5 + data.Length] = Xor(f, 2, len - 1);
        f[6 + data.Length] = T; f[7 + data.Length] = T;
        return f;
    }

    private void LogHex(string direction, byte[] bytes) => Log(direction, BitConverter.ToString(bytes).Replace("-", " "));
    private void Log(string level, string message)
    {
        if (IsDisposed) return;
        void Append()
        {
            logBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }
        if (InvokeRequired) { try { BeginInvoke((Action)Append); } catch { } }
        else Append();
    }
}
