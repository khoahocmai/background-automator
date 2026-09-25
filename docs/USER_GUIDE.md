# Hướng Dẫn Sử Dụng BackgroundAutomator

Tài liệu hướng dẫn sử dụng ứng dụng **BackgroundAutomator** dành cho người dùng cuối trên hệ điều hành Windows.

---

## 1. Giới thiệu

**BackgroundAutomator** là tiện ích Windows (chuẩn 64-bit / x64) cho phép gửi thao tác click chuột và chuỗi macro tự động tới một cửa sổ hoặc control cụ thể ở chế độ chạy ngầm (**Background**).

### Điểm nổi bật:
* **Không chiếm chuột vật lý**: Ứng dụng gửi trực tiếp thông điệp chuột qua Win32 API (`PostMessage`) tới handle (`HWND`) của cửa sổ mục tiêu. Chuột thật của bạn hoàn toàn tự do để làm việc khác, lướt web hoặc soạn thảo văn bản.
* **Hoạt động khi cửa sổ bị che khuất**: Cửa sổ mục tiêu không cần đưa lên trên cùng (foreground) hay active.
* **Hỗ trợ Macro & Nhận diện màu sắc (WaitColor)**: Chờ điểm ảnh đổi sang màu chỉ định mà không can thiệp màn hình thật.
* **Lưu trữ thông minh**: Profile lưu thông tin nhận diện cửa sổ (Process, Title, Class) thay vì HWND tạm thời, tự động nhận diện lại khi mở lại phần mềm.

---

## 2. Cách chạy ứng dụng

### 2.1. Yêu cầu hệ thống
* Hệ điều hành: Windows 10 (version 1703 trở lên) hoặc Windows 11 (bản 64-bit x64).

### 2.2. Khởi chạy
* Nếu bạn sử dụng bản phát hành portable tự đóng gói (self-contained) trong thư mục `publish/`:
  1. Mở thư mục chứa file phát hành.
  2. Nhấp đúp chạy trực tiếp:
     ```text
     BackgroundAutomator.App.exe
     ```
  3. **Không cần cài đặt thêm .NET Runtime** vì toàn bộ thư viện cần thiết đã được tích hợp sẵn.

---

## 3. Chọn Target (Cửa sổ / Control mục tiêu)

Tab đầu tiên khi mở ứng dụng là **Target Inspector**. Đây là nơi bạn chọn cửa sổ cần click.

<!-- TODO: Screenshot giao diện Target Inspector -->

### 3.1. Hai cách chọn Target
1. **Chọn từ danh sách (Dropdown)**:
   * Nhấp **Refresh List** để nạp danh sách các cửa sổ đang mở.
   * Chọn cửa sổ mong muốn từ menu thả xuống **Select Window**.
2. **Kéo tâm ngắm (Crosshair - Khuyên dùng cho control con)**:
   * Nhấn giữ chuột trái vào nút **`◎ Drag crosshair to target window / control`**.
   * Kéo con trỏ chuột đến nút bấm, ô nhập liệu hoặc panel bên trong ứng dụng mục tiêu.
   * Thả chuột ra để khóa target. Ứng dụng sẽ nhận diện control con sâu nhất tại vị trí thả chuột.

### 3.2. Hiểu về các thông số hiển thị
* **Root HWND**: Mã handle của cửa sổ gốc cấp cao nhất (ví dụ: cửa sổ chương trình chính).
* **Target HWND**: Mã handle trực tiếp nhận sự kiện click (có thể là nút bấm con nằm sâu bên trong panel).
* **Parent HWND**: Handle của khung chứa trực tiếp của control đó.
* **Screen Coords vs Client Coords**:
  * **Screen Coords**: Tọa độ tuyệt đối trên toàn bộ màn hình desktop.
  * **Client Coords (Tọa độ Client)**: Tọa độ tương đối tính từ góc trên-bên trái `(0, 0)` của chính control mục tiêu. BackgroundAutomator gửi click dựa trên tọa độ Client này, do đó khi bạn di chuyển cửa sổ mục tiêu trên màn hình, click vẫn trúng đích chính xác.

### 3.3. Khi cửa sổ mục tiêu bị đóng
Nếu ứng dụng mục tiêu bị đóng trong lúc đang chọn hoặc đang click, BackgroundAutomator sẽ lập tức phát hiện thông qua Win32 API, chuyển trạng thái hiển thị thành `Target unavailable (window closed)` và tự động dừng click an toàn, không gây crash chương trình.

---

## 4. Simple Mode (Click tuần tự)

Chế độ **Simple Mode** dùng khi bạn chỉ cần click lặp đi lặp lại vào một hoặc nhiều tọa độ cố định.

<!-- TODO: Screenshot giao diện Simple Mode -->

### 4.1. Thêm điểm click vào danh sách
1. **Lấy tọa độ từ Inspector**: Nhấp nút **`+ Add Target Point (From Inspector)`** để lấy ngay điểm bạn vừa thả tâm ngắm ở tab Target Inspector.
2. **Thêm tọa độ tùy chỉnh**:
   * Nhập tọa độ `X` và `Y` (tọa độ Client của target).
   * Chọn kiểu click: **Single Click** (click đơn) hoặc **Double Click** (click đúp).
   * Nhấp nút **`+ Add Custom X/Y Point`**.
3. **Sắp xếp thứ tự**:
   * Chọn một điểm trong danh sách và bấm **Move Up ▲** hoặc **Move Down ▼** để đổi thứ tự thực thi.
   * Nhấp **Remove** để xóa điểm được chọn, hoặc **Clear All** để xóa toàn bộ.

### 4.2. Cài đặt thực thi
* **Interval**: Khoảng thời gian chờ giữa mỗi lần click (tính bằng mili-giây, mặc định là 500 ms).
* **Repeat**:
  * **Until stopped**: Chạy liên tục cho đến khi bạn bấm dừng.
  * **Count**: Dừng lại sau khi hoàn thành đúng số lượt chu kỳ chỉ định (ví dụ: 10 lần).

### 4.3. Bắt đầu và Dừng
* Nhấp nút **▶ Start (F6)** để bắt đầu click.
* Nhấp nút **⏹ Stop (F7)** để dừng lại.

### 4.4. Ví dụ cấu hình thực tế
Giả sử bạn cần click vào nút "Nhiệm vụ" (tọa độ Client 45, 20) rồi click vào nút "Nhận thưởng" (tọa độ Client 120, 60), lặp lại mỗi 2 giây:
1. Nhập X=45, Y=20, chọn *Single Click* → Nhấp *Add Custom X/Y Point*.
2. Nhập X=120, Y=60, chọn *Single Click* → Nhấp *Add Custom X/Y Point*.
3. Đặt *Interval* = `2000` ms.
4. Chọn *Until stopped*.
5. Bấm **Start (F6)**.

---

## 5. Phím tắt toàn cục (Global Hotkeys)

BackgroundAutomator hỗ trợ phím tắt toàn hệ thống, hoạt động ngay cả khi bạn đang thu nhỏ ứng dụng xuống Taskbar:

| Phím tắt | Chức năng | Mô tả |
|---|---|---|
| **F6** | **Start / Stop** | Bật hoặc tắt chu trình click / macro đang chạy. |
| **F7** | **Emergency Stop** | Dừng khẩn cấp toàn bộ thao tác click ngay lập tức. |

> [!NOTE]
> Phím tắt được đăng ký ở cấp độ Windows. Nếu một ứng dụng khác đã chiếm quyền ưu tiên hai phím này, ứng dụng sẽ ghi cảnh báo vào tab Diagnostic Log.

---

## 6. Macro Mode (Kịch bản tự động hóa nâng cao)

Chế độ **Macro Mode** cho phép kết hợp các hành động phức tạp theo kịch bản logic gồm: Click, DoubleClick, PressKey, Delay, WaitColor, WaitForText và SafeAutoConfirm.

<!-- TODO: Screenshot giao diện Macro Mode -->

### 6.1. Các hành động hỗ trợ
* **Click**: Click đơn vào tọa độ (X, Y).
* **DoubleClick**: Click đúp vào tọa độ (X, Y).
* **PressKey**: Nhấn phím bàn phím ở chế độ ngầm (`Enter`, `Tab`, `Escape`, `Space`, các phím mũi tên) qua Win32 `PostMessage` mà không chiếm chuột/bàn phím thật.
* **Delay**: Tạm dừng kịch bản trong khoảng thời gian chỉ định (ví dụ: 500 ms) trước khi sang bước tiếp theo.
* **WaitColor**: Liên tục kiểm tra màu của điểm ảnh tại tọa độ (X, Y) của cửa sổ mục tiêu:
  * **Color (RGB)**: Màu sắc cần xuất hiện (chọn qua bảng màu trực quan).
  * **Tolerance (Tol)**: Độ sai lệch màu cho phép (từ 0 đến 255; khuyến nghị 5–15 nếu game/ứng dụng có đổ bóng hoặc khử răng cưa).
  * **Timeout**: Thời gian tối đa cho phép chờ (ms). Nếu hết thời gian mà màu chưa xuất hiện, macro sẽ dừng lại báo Timeout.
  * **Poll Interval**: Tần suất kiểm tra định kỳ (khoảng 50 ms/lần).
* **WaitForText**: Chờ chuỗi ký tự xuất hiện trên cửa sổ thông qua Windows UI Automation (UIA) với độ chính xác tuyệt đối (không lỗi nhận diện ký tự như OCR):
  * **Expected Text**: Văn bản cần xuất hiện (hỗ trợ chế độ tìm kiếm `Contains` hoặc `Exact`).
  * **Scope**: Giới hạn trong vùng hiển thị thực tế (`VisibleViewportOnly`) để tránh đọc trúng các dòng văn bản cũ đã cuộn mất.
* **SafeAutoConfirm**: Tự động phát hiện và xác nhận lệnh Terminal có kiểm duyệt (xem chi tiết mục 6.3).

### 6.2. Ví dụ tạo kịch bản thực tế
Kịch bản: Click mở hộp quà → Chờ 500ms → Đợi thanh trạng thái chuyển màu Xanh lá (Green) → Click đúp để xác nhận:

```text
1. Click (X: 100, Y: 150)
   ↓
2. Delay 500 ms
   ↓
3. WaitColor Green (X: 250, Y: 80, Tol: 10, Timeout: 5000 ms)
   ↓
4. DoubleClick (X: 300, Y: 200)
```

**Thao tác trong giao diện**:
1. Nhập X=100, Y=150 → Bấm **+ Add Click**.
2. Nhập `500` vào ô ms → Bấm **+ Add Delay**.
3. Nhập X=250, Y=80, bấm nút **Color** chọn màu Xanh lá, chỉnh Tol=10, Timeout=5000 → Bấm **+ Add WaitColor**.
4. Nhập X=300, Y=200 → Bấm **+ Add DoubleClick**.
5. Bấm **▶ Run Macro** để khởi chạy kịch bản.

---

### 6.3. Tính năng Safe Auto-Confirm (Xác nhận lệnh Terminal có kiểm duyệt)

Tính năng chuyên biệt cho phép tự động phê duyệt các hộp thoại xác nhận chạy lệnh của AI Agent / CLI (ví dụ Antigravity hoặc Claude Code):

```text
Run this command?
> 1. Yes, run command
```

#### Nguyên tắc An toàn Tuyệt đối (Fail-Closed):
1. **Không phê duyệt mù quáng**: Phần mềm không bao giờ bấm Enter tự do mỗi khi nhìn thấy chữ `Run this command?`.
2. **Không dùng wildcard toàn bộ**: Chỉ cho phép chạy đúng câu lệnh nằm trong danh sách cho phép (Allowlist) với chế độ so khớp chính xác (`Exact`).
3. **Kiểm tra đa điều kiện**: Bắt buộc thỏa mãn đồng thời:
   - Tiến trình mục tiêu khớp (`ExpectedProcess`, ví dụ: `WindowsTerminal`).
   - Câu hỏi xác nhận đang hiển thị (`ExpectedPrompt`, ví dụ: `Run this command?`).
   - Lựa chọn đồng ý đang được chọn (`ExpectedSelectedOption`, ví dụ: `Yes, run command`).
   - Câu lệnh trích xuất được khớp chính xác với `AllowedCommand`.
   - **Xử lý lệnh nhiều dòng & mập mờ (Fail-Closed)**: Bộ phân tích hỗ trợ các lệnh nối dòng (`|`, `` ` ``, `\`, dòng thụt lề) và wrapper (`● Bash(...)`). Nếu phát hiện nhiều ứng viên lệnh độc lập hoặc định dạng không rõ ràng, hệ thống sẽ chặn với lý do `AmbiguousPrompt` thay vì đoán bừa.

#### Chế độ Observe-Only (Chỉ quan sát — Mặc định):
- Khi thêm một hành động Safe Auto-Confirm mới, phần mềm mặc định đặt chế độ là **Observe-Only**.
- Ở chế độ này, bộ nhận diện vẫn chạy, phân tích câu lệnh và đối chiếu Allowlist:
  - Nếu hợp lệ: Ghi log `[ObserveOnly] WOULD APPROVE: <command>` và kết thúc thành công.
  - Tuyệt đối **KHÔNG gửi phím Enter** và **KHÔNG kích hoạt cửa sổ**.
- Giúp người dùng kiểm chứng độ chính xác của bộ lọc trước khi chuyển sang chế độ thực thi thật.

#### Cơ chế Foreground Pulse (Kích hoạt chớp nhoáng & Phục hồi):
- Modern Terminal (Windows Terminal / ConPTY) không nhận phím gửi ngầm qua `PostMessage`.
- Ở chế độ **Confirm**, phần mềm thực hiện quy trình bảo vệ nhiều lớp:
  1. **Kiểm tra đặc quyền (UIPI Guard)**: Nếu BackgroundAutomator chạy quyền thường (standard user) trong khi Windows Terminal chạy Administrator, hệ thống lập tức chặn với lý do `UipiMismatch`, tuyệt đối không cố kích hoạt cửa sổ hay gửi phím.
  2. **Kiểm tra cửa sổ Terminal**: Nếu đang bị thu nhỏ (Minimized), hủy bỏ an toàn (`TargetUnavailable`).
  3. Ghi nhớ handle cửa sổ bạn đang làm việc (`previousForeground`).
  4. Đưa Terminal lên trên cùng (`SetForegroundWindow`) và đợi xác nhận đã active.
  5. **Kiểm tra lại toàn diện lần 2**: Đọc lại vùng nhìn thấy (`VisibleViewportOnly`), bóc tách lại câu lệnh, đối chiếu định danh tiến trình và đánh giá lại quy tắc. Nếu có bất kỳ thay đổi nào, hủy bỏ ngay lập tức.
  6. **Thu hẹp tranh chấp tiêu điểm (Focus Race Guard)**: Ngay sát trước lúc gọi `SendInput`, kiểm tra đồng bộ (không có độ trễ async) để xác nhận cửa sổ Terminal vẫn đang giữ tiêu điểm. Nếu một cửa sổ khác bất ngờ nhảy lên cướp tiêu điểm, lập tức hủy bỏ. (Lưu ý: cơ chế cooperative focus của Windows Win32 có giới hạn nền tảng nếu có tiến trình khác can thiệp cùng microsecond, nhưng khoảng thời gian rủi ro đã được triệt tiêu tối đa).
  7. Gửi duy nhất 1 phím Enter qua `SendInput` (tuyệt đối không retry gửi phím lần hai).
  8. Chờ fingerprint của prompt hiện tại biến mất (tối đa 2.000 ms) để xác nhận Terminal đã tiếp nhận lệnh.
  9. **Khôi phục tiêu điểm**: Tự động trả tiêu điểm về cửa sổ bạn đang làm việc dở dang trong khối `finally` (nếu cửa sổ cũ vẫn còn tồn tại).

---

## 7. Quản lý Profile (Lưu & Tải cấu hình)

Tab **Profiles** giúp lưu lại toàn bộ thiết lập để tái sử dụng mà không cần cấu hình lại từ đầu.

<!-- TODO: Screenshot giao diện Profiles -->

### 7.1. Cơ chế lưu trữ thông minh (Target Re-resolution)
* **Không lưu HWND tạm bợ**: Trong Windows, mỗi lần mở lại một phần mềm, hệ điều hành sẽ cấp một mã HWND hoàn toàn mới. Nếu lưu mã HWND cũ, phần mềm sẽ không thể click được sau khi restart máy.
* **Durable Target Descriptors**: BackgroundAutomator lưu lại thông tin nhận dạng bền vững gồm: tên tiến trình (`ProcessName`), tiêu đề cửa sổ (`WindowTitle`), lớp cửa sổ (`WindowClass`) và thông số control con.
* Khi bạn tải lại Profile, ứng dụng sẽ tự động dò tìm lại cửa sổ mới tương ứng và cập nhật HWND mới tức thì.

### 7.2. Các thao tác Profile
1. **Lưu Profile (Save Profile)**:
   * Nhập tên cấu hình vào ô **Profile Name** (ví dụ: `AutoFarm`).
   * Chọn chế độ tương ứng: **Simple Mode** hoặc **Macro Mode**.
   * Nhấp **💾 Save Profile**. Dữ liệu được ghi an toàn dạng JSON tại `%LOCALAPPDATA%\BackgroundAutomator\profiles\`.
2. **Tải Profile (Load Profile)**:
   * Chọn Profile trong danh sách bên dưới.
   * Nhấp **📂 Load Profile**. Các điểm click, thiết lập lặp lại hoặc chuỗi macro sẽ được khôi phục nguyên vẹn.
3. **Dò lại mục tiêu (Re-resolve Target)**:
   * Nếu bạn vừa khởi động lại game hoặc ứng dụng mục tiêu, chọn Profile rồi nhấp **🔄 Re-resolve Target** để liên kết với tiến trình mới.
4. **Xóa Profile (Delete Profile)**:
   * Chọn Profile không còn dùng đến và bấm **🗑 Delete Profile**.

---

## 8. Xử lý Target chạy quyền Administrator (UIPI)

### 8.1. Cơ chế bảo mật UIPI của Windows
Windows có cơ chế bảo vệ mang tên **User Interface Privilege Isolation (UIPI)**:
* Một ứng dụng chạy ở quyền người dùng thông thường (**Standard User**) **không được phép** gửi thông điệp cửa sổ (như click chuột) sang một ứng dụng đang chạy ở quyền Quản trị viên (**Administrator** / Elevated).

### 8.2. Dấu hiệu và cách khắc phục
* Khi bạn chọn một cửa sổ đang chạy quyền Administrator, BackgroundAutomator sẽ lập tức phát hiện và hiện huy hiệu cảnh báo màu vàng/đỏ:
  `Target Elevated (Admin) — Action Required: Run as Admin`
* **Cách xử lý**: Nhấp vào nút **`Restart as Administrator`** hiển thị ngay cạnh dòng trạng thái. BackgroundAutomator sẽ tự khởi động lại với quyền Administrator, cho phép bạn tương tác bình thường với cửa sổ mục tiêu.

---

## 9. Các giới hạn kỹ thuật cần biết

Do hoạt động trên nền tảng Win32 Message API (`PostMessage`), BackgroundAutomator có một số giới hạn tự nhiên của hệ điều hành:

1. **Game dùng DirectInput / Raw Input**:
   * Các game 3D bắn súng, hành động đọc trực tiếp tín hiệu phần cứng từ driver chuột thay vì thông qua hàng đợi thông điệp Windows sẽ không nhận được click ngầm.
2. **Ứng dụng Chromium / Electron / WPF nguyên khối**:
   * Các ứng dụng như Chrome, Discord, VS Code chỉ có một HWND duy nhất cho toàn bộ giao diện; các nút bấm bên trong vẽ bằng canvas hoặc web engine nên không có HWND con riêng biệt. Muốn click cần sử dụng tọa độ tương đối tính từ cửa sổ gốc.
3. **Bề mặt tăng tốc phần cứng (DirectComposition / Exclusive Fullscreen)**:
   * Cửa sổ game chạy chế độ độc quyền toàn màn hình (Exclusive Fullscreen) có thể chặn `PrintWindow`, khiến tính năng `WaitColor` không chụp được điểm ảnh.
4. **Tính không ngắt được của lệnh Win32 PrintWindow**:
   * Khi lệnh chụp ảnh của Windows đang chạy trong tầng nhân (kernel), nếu phần mềm mục tiêu bị đơ cứng, lệnh này cần đợi Windows xử lý xong chứ không thể hủy ép buộc ngay lập tức. BackgroundAutomator đã tích hợp cơ chế phát hiện ứng dụng bị treo (`IsHungAppWindow`) để chủ động bỏ qua trước khi chụp.

---

## 10. Bảng khắc phục sự cố (Troubleshooting)

| Hiện tượng | Nguyên nhân có thể | Cách xử lý |
|---|---|---|
| **Click không có tác dụng** | Cửa sổ mục tiêu không xử lý `WM_LBUTTONDOWN` ở chế độ nền, hoặc sử dụng DirectInput/Raw Input. | Kiểm tra xem ứng dụng có hỗ trợ click nền không; thử click vào ứng dụng chuẩn như Notepad hoặc `TestTarget` để đối chiếu. |
| **Sai tọa độ click** | Nhầm lẫn giữa tọa độ màn hình (Screen) và tọa độ tương đối của cửa sổ (Client). | Dùng công cụ tâm ngắm kéo thả trực tiếp vào control đích để lấy đúng tọa độ Client `(X, Y)`. |
| **Target chạy Administrator (Click bị chặn)** | Cơ chế bảo vệ UIPI của Windows ngăn ứng dụng thường gửi tin nhắn tới ứng dụng Admin. | Nhấp nút **Restart as Administrator** trên giao diện BackgroundAutomator để cấp quyền tương đương. |
| **WaitColor bị Timeout** | Màu sắc thực tế tại điểm ảnh không khớp với mã màu đã chọn, hoặc cửa sổ bị che mất vùng hiển thị. | Tăng giá trị **Tolerance** (ví dụ: từ 5 lên 15–20); kiểm tra lại tọa độ X, Y có bị lệch điểm màu không. |
| **Báo lỗi CaptureFailed** | Cửa sổ mục tiêu đang bị thu nhỏ (Minimized), có diện tích bằng 0, hoặc card đồ họa từ chối lệnh `PrintWindow`. | Đảm bảo cửa sổ mục tiêu đang mở ở kích thước bình thường (có thể bị cửa sổ khác che nhưng không được thu nhỏ xuống Taskbar). |
| **Báo lỗi Target unavailable** | Ứng dụng mục tiêu đã bị tắt hoặc crash đột ngột. | Mở lại ứng dụng mục tiêu, sau đó dùng nút **Re-resolve Target** hoặc chọn lại từ menu. |
| **Hotkey F6/F7 không phản hồi** | Phím tắt F6 hoặc F7 đang bị phần mềm khác chiếm độc quyền (ví dụ: trình quay màn hình, game). | Kiểm tra tab **Diagnostic Log** xem có dòng thông báo `Failed to register global hotkey` hay không; tắt ứng dụng xung đột phím tắt. |
| **Profile load nhưng không tìm thấy target** | Ứng dụng mục tiêu chưa được mở, hoặc tiêu đề cửa sổ đã thay đổi (ví dụ: tên file trên tiêu đề thay đổi). | Mở ứng dụng mục tiêu lên trước, sau đó bấm **🔄 Re-resolve Target** trong tab Profiles. |

---

## 11. Quick Start (Quy trình 30 giây)

```text
Mở BackgroundAutomator
   ↓
Mở ứng dụng cần click (ví dụ: TestTarget hoặc game)
   ↓
Chuyển sang tab "Target Inspector" → Nhấn giữ "◎ Drag crosshair..." kéo thả vào nút bấm
   ↓
Chuyển sang tab "Simple Mode"
   ↓
Bấm "+ Add Target Point (From Inspector)"
   ↓
Đặt Interval (ví dụ: 500 ms) và chọn "Until stopped"
   ↓
Bấm "▶ Start" (hoặc bấm phím F6)
   ↓
Bấm phím F7 bất kỳ lúc nào để dừng khẩn cấp
```
