# ESP32-C3 MQTT Configuration Tool

## Yêu cầu
- Windows 10/11
- .NET 8 SDK
- Driver USB-UART phù hợp

## Build và chạy
Mở PowerShell tại thư mục chứa `.csproj`:
```powershell
dotnet restore
dotnet build -c Release
dotnet run
```

## Chức năng
- Chọn COM và baud rate (mặc định 115200, 8-N-1, no flow control).
- Nút Kết nối UART1 / Ngắt kết nối.
- Nhập Broker URI, Username, Password.
- Khung thông tin tổng hợp và nút Copy toàn bộ cấu hình.
- **Lưu TXT / Mở TXT**: file UTF-8 dạng `key=value`, có thể đọc trực tiếp bằng Notepad.
- **Lưu Excel / Mở Excel**: file `.xlsx` thực, sheet `MQTT Config`, cần kết nối Internet lần đầu để NuGet tải ClosedXML khi `dotnet restore`.
- Nút Thiết lập ESP32-C3.
- Hiển thị ✓ OK riêng cho Username, Password, Broker URI và SAVE; mục lỗi được đánh dấu NG; cuối quy trình hiện KẾT QUẢ: OK hoặc NG.
- Nhận ACK nhị phân, xác thực header/length/checksum/tail; log TX/RX HEX.

## Lưu ý bảo mật
File TXT và XLSX chứa thông tin đăng nhập có thể khôi phục/đọc được. Base64 trong TXT chỉ là mã hóa biểu diễn, **không phải mã hóa bảo mật**. Không gửi các file này qua kênh công khai.

## Giao thức UART
Commands: username `0x0001`, password `0x0002`, SAVE `0x0003`, broker URI `0x0004`.
ACK tương ứng: `0x0081`, `0x0082`, `0x0083`, `0x0084`. Status `0x00` là thành công.
