"""Vietnamese UI strings (public site)."""

MESSAGES = {
    "nav.home": "Giới thiệu",
    "nav.download": "Tải plugin",
    "nav.buy": "Mua license",
    "lang.vi": "Tiếng Việt",
    "lang.en": "English",
    "footer.rights": "Bản quyền",
    "footer.terms": "Điều khoản dịch vụ",
    "footer.privacy": "Chính sách quyền riêng tư",
    "footer.refund": "Chính sách hoàn tiền",
    "meta.terms": "Điều khoản dịch vụ Body Exporter — license add-in SolidWorks.",
    "meta.privacy": "Chính sách quyền riêng tư Body Exporter — website, plugin và thanh toán.",
    "meta.refund": "Chính sách hoàn tiền license Body Exporter — phần mềm số.",
    "home.eyebrow": "Add-in SolidWorks",
    "home.hero_title_default": "SolidWorks Body Exporter",
    "home.hero_subtitle_default": (
        "Xuất thông tin body/part từ SolidWorks — Excel, template, workflow nhanh cho xưởng mộc."
    ),
    "home.bullets_default": (
        "Kéo thả kích thước L×W×T\n"
        "Export Excel & template {{placeholder}}\n"
        "License online — trial 14 ngày"
    ),
    "home.cta_download": "Tải plugin",
    "home.cta_buy": "Mua license",
    "download.title": "Tải plugin",
    "download.version": "Phiên bản",
    "download.policy_title": "Chính sách dữ liệu",
    "download.policy_hint": "Bấm để xem chi tiết",
    "download.policy_lead": (
        "Body Exporter thu thập một số thông tin kỹ thuật tối thiểu để plugin hoạt động ổn định — "
        "kích hoạt license, hỗ trợ khi có lỗi và cải thiện phần mềm theo thời gian."
    ),
    "download.policy_p2": (
        "Thông tin có thể được ghi nhận gồm: mã nhận diện máy (dùng để gắn license), "
        "phiên bản plugin và SolidWorks đang dùng, thời điểm cài đặt và lần dùng gần nhất, "
        "địa chỉ IP để ước lượng khu vực (thành phố/quốc gia), và trạng thái license hiện tại."
    ),
    "download.policy_safe": (
        "Chúng tôi <strong>không đọc, không lưu và không gửi</strong> bất kỳ nội dung nào từ file "
        "SolidWorks của bạn — bao gồm model 3D, tên part hay file export."
    ),
    "download.policy_foot": (
        "Dữ liệu thu thập chỉ dùng nội bộ, không phục vụ quảng cáo và không chia sẻ cho bên thứ ba. Liên hệ:"
    ),
    "download.consent": "Tôi đã đọc và đồng ý với <strong>Chính sách dữ liệu</strong>.",
    "download.btn": "Tải bản {version}",
    "download.unavailable": (
        "File đã được gỡ để gỡ lỗi. Vui lòng liên hệ "
        '<a href="mailto:{email}">{email}</a> để được hỗ trợ.'
    ),
    "download.policy_error": "Bạn cần đồng ý chính sách thu thập dữ liệu để tải plugin.",
    "download.policy_error_unavailable": "File đã được gỡ để gỡ lỗi. Vui lòng liên hệ {email} để được hỗ trợ.",
    "download.guide_install": "Hướng dẫn cài đặt",
    "download.guide_use": "Hướng dẫn sử dụng",
    "download.guide_excel": "Export Excel & placeholder",
    "download.guide_notes": "Ghi chú thêm",
    "download.install_1": "<strong>Đóng SolidWorks</strong> (tránh DLL bị khóa khi cài).",
    "download.install_2": (
        "Giải nén ZIP → chạy <code>Install-BodyExporter.cmd</code> <strong>Run as administrator</strong>."
    ),
    "download.install_3": (
        "Mở SolidWorks → <strong>Tools → Add-Ins</strong> → bật <em>SolidWorks Body Exporter</em>."
    ),
    "download.install_4": "Dùng shortcut <strong>Body Exporter</strong> trên Desktop để mở cửa sổ export.",
    "download.use_1": "Mở file <strong>.SLDPRT</strong> trong SolidWorks.",
    "download.use_2": "Mở <strong>Body Exporter</strong> (shortcut Desktop hoặc launcher trong thư mục cài).",
    "download.use_3": (
        "Plugin quét danh sách body — chỉnh <strong>tên hiển thị</strong> và trục "
        "<strong>Length / Width / Thickness</strong>."
    ),
    "download.use_4": (
        "<strong>Copy All</strong> — dán vào Excel; hoặc menu <strong>Export</strong> → xuất file Excel."
    ),
    "download.use_5": (
        "Lần đầu: <strong>License</strong> → trial 14 ngày (cần internet) hoặc key từ <a href=\"/buy\">/buy</a>."
    ),
    "download.use_foot": "Bấm Save trong cửa sổ export để lưu metadata vào file Part.",
    "download.excel_lead": (
        "Tạo file <code>.xlsx</code> mẫu của công ty, chèn <strong>placeholder</strong> vào ô cần điền — "
        "plugin thay bằng dữ liệu từng body khi export."
    ),
    "download.excel_1": (
        "<strong>Export ▾ → Excel template panel…</strong> — <em>Add new…</em> / <em>Change…</em> "
        "chọn file mẫu (một dòng có placeholder)."
    ),
    "download.excel_2": "<strong>Export ▾ → Fill Excel template…</strong> — chọn nơi lưu file kết quả.",
    "download.excel_3": "Body đầu tiên ghi đè dòng placeholder; các body tiếp theo chèn xuống các dòng bên dưới.",
    "download.excel_table_title": "Placeholder hỗ trợ (không phân biệt hoa thường):",
    "download.excel_th_token": "Gõ trong Excel",
    "download.excel_th_data": "Dữ liệu điền",
    "download.excel_row_name": "Tên body / part",
    "download.excel_row_len": "Chiều dài (mm)",
    "download.excel_row_wid": "Chiều rộng (mm)",
    "download.excel_row_thk": "Chiều dày (mm)",
    "download.excel_row_qty": "Số lượng",
    "download.excel_row_app": "Màu / vật liệu",
    "download.excel_row_prev": "Ảnh thumbnail (PNG trong ô)",
    "download.excel_foot": "Placeholder lạ sẽ báo sau khi export.",
    "download.trial_foot": "Trial 14 ngày sau khi cài — cần internet lần đầu.",
    "download.trial_buy": "Mua license",
    "meta.home": (
        "Body Exporter — add-in SolidWorks xuất body/part, Excel & template, license online. Trial 14 ngày."
    ),
    "meta.download": (
        "Tải plugin Body Exporter cho SolidWorks — export Excel, template {{placeholder}}, hướng dẫn cài đặt."
    ),
    "meta.buy": (
        "Mua license Body Exporter — chuyển khoản QR hoặc thẻ. License gửi email sau thanh toán."
    ),
    "meta.buy_success": "Cảm ơn bạn đã mua Body Exporter. License sẽ được gửi qua email.",
    "buy.title": "Mua license",
    "buy.pricing_title": "Bảng giá",
    "buy.pricing_product": "Body Exporter — License cá nhân (1 máy)",
    "buy.pricing_amount_label": "Giá",
    "buy.pricing_vn_sub": "Thanh toán quốc tế: khoảng ${usd} USD",
    "buy.pricing_intl_sub": "Tham khảo: {vnd} VND nếu chuyển khoản trong Việt Nam",
    "buy.pricing_intl_pending": "Liên hệ để biết giá quốc tế",
    "buy.pricing_term": "Thời hạn license: {days} ngày kể từ khi kích hoạt",
    "buy.pricing_f1": "Xuất body/part SolidWorks → Excel & template",
    "buy.pricing_f2": "Gửi license key tự động qua email sau thanh toán",
    "buy.pricing_f3": "Dùng thử 14 ngày khi cài plugin (lần đầu cần internet)",
    "buy.pricing_f4": "Hỗ trợ qua email sau khi mua",
    "buy.pricing_legal": (
        'Xem <a href="/terms-and-conditions">Điều khoản</a>, '
        '<a href="/privacy">Quyền riêng tư</a>, <a href="/refund">Hoàn tiền</a>.'
    ),
    "buy.checkout_title": "Thanh toán",
    "buy.step_label_email": "1. Email",
    "buy.step_label_pay": "2. Thanh toán",
    "buy.btn_continue": "Tiếp tục",
    "buy.paddle_opening": "Đang mở cổng thanh toán Paddle…",
    "buy.paddle_overlay_hint": "Cửa sổ thanh toán Paddle sẽ bật lên. Nếu không thấy, cho phép pop-up hoặc bấm «Mở cửa sổ thanh toán» bên dưới.",
    "buy.paddle_open_window": "Mở cửa sổ thanh toán Paddle",
    "buy.paddle_js_missing": "Không tải được Paddle.js (cdn.paddle.com). Tắt VPN/AdBlock rồi F5.",
    "buy.paddle_timeout": "Cổng thanh toán không phản hồi. Thử «Mở cửa sổ thanh toán» hoặc VietQR.",
    "buy.paddle_domain_hint": (
        "Paddle live: domain <code>bodyexporter.com</code> phải được Paddle <strong>duyệt</strong> "
        "(Checkout → Checkout settings → Default payment link = <code>https://bodyexporter.com/buy/paddle</code>). "
        "Chưa duyệt → lỗi «Something went wrong»."
    ),
    "buy.btn_edit_email": "Sửa email",
    "buy.step2_email_sent": "License gửi tới",
    "buy.step2_vn_lead": "Quét QR hoặc chuyển khoản đúng số tiền và nội dung CK bên dưới.",
    "buy.step2_intl_lead": "Bước 2: thanh toán <strong>${usd} USD</strong> bằng thẻ / PayPal.",
    "buy.paddle_page_title": "Thanh toán quốc tế",
    "buy.paddle_page_lead": "Thanh toán an toàn qua Paddle (thẻ / PayPal).",
    "buy.paddle_back": "← Quay lại trang mua",
    "buy.paddle_create_fail": "Không tạo được phiên thanh toán Paddle ({error}). Kiểm tra env trên server.",
    "buy.paddle_default_link": (
        "Paddle chưa có Default payment link. Vào Paddle → Checkout → Checkout settings "
        "→ đặt https://bodyexporter.com/buy/paddle (không phải /webhook/paddle)."
    ),
    "buy.paddle_network": (
        "<strong>Cổng Paddle không tải được</strong> (<code>checkout-service.paddle.com</code>). "
        "Nếu điện thoại 4G vẫn lỗi thì thường do mạng VN/ISP hoặc tài khoản Paddle — không phải lỗi website. "
        "Khách Việt Nam: dùng <strong>VietQR</strong> bên dưới. "
        "Default payment link Paddle = <code>https://bodyexporter.com/buy/paddle</code>."
    ),
    "buy.paddle_use_vn": "Thanh toán VietQR (khuyến nghị tại VN)",
    "buy.paddle_retry": "Thử lại Paddle",
    "buy.paddle_sepay_fallback": "Thanh toán thẻ qua SePay (VND)",
    "buy.paddle_brave": (
        "<strong>VeePN / VPN có AdBlock:</strong> tắt <strong>AdBlock</strong> trong extension VeePN (công tắt màu xanh), "
        "hoặc ngắt VPN hẳn khi test Paddle. AdBlock trong VPN hay chặn <code>checkout-service.paddle.com</code>. "
        "Edge: icon 🔒 → Tracking prevention → <strong>Tắt</strong> cho trang này, rồi <strong>F5</strong>."
    ),
    "buy.paddle_vn_fallback": "Hoặc thanh toán VietQR (VND) trên trang mua",
    "buy.paddle_support": (
        "Vẫn lỗi? Gửi email <a href=\"mailto:{email}?subject=Paddle%20{txn}\">{email}</a> "
        "kèm mã <code>{txn}</code>."
    ),
    "buy.pay_mode_label": "Phương thức thanh toán",
    "buy.mode_vn": "VietQR · chuyển khoản",
    "buy.mode_intl": "Quốc tế · thẻ",
    "buy.enter_email_hint": "Nhập email hợp lệ để hiện hình thức thanh toán.",
    "buy.qr_loading": "Đang tải QR…",
    "buy.qr_fail": "Không tạo được QR. Kiểm tra email.",
    "buy.net_fail": "Lỗi mạng. Thử lại.",
    "buy.paddle_loading": "Cổng thanh toán đang tải. Đợi vài giây rồi thử lại.",
    "buy.paddle_fail": "Không mở được thanh toán. Liên hệ {email}",
    "buy.intl_lead": "Bạn sẽ thanh toán <strong>${usd} USD</strong> (thẻ / PayPal). License gửi email sau khi thanh toán thành công.",
    "buy.paddle_hint": "Thanh toán quốc tế an toàn.",
    "buy.btn_paddle": "Thanh toán ${usd} USD",
    "buy.intl_unavailable": "Thanh toán quốc tế tạm chưa bật. Liên hệ <a href=\"mailto:{email}\">{email}</a>.",
    "buy.vietqr_unavailable": "Chuyển khoản VN tạm chưa cấu hình. Liên hệ <a href=\"mailto:{email}\">{email}</a>.",
    "buy.checkout_unavailable": "Chưa cấu hình thanh toán. Email <a href=\"mailto:{email}\">{email}</a>.",
    "buy.intro_default": (
        "Nhập email để nhận license tự động sau khi thanh toán. "
        "Chọn chuyển khoản QR hoặc thẻ bên dưới."
    ),
    "buy.footer_default": (
        "<p>Sau khi chuyển khoản đúng số tiền và nội dung CK, license gửi về email trong vài phút. "
        "Cần hỗ trợ: <a href=\"mailto:hotro@bodyexporter.com\">hotro@bodyexporter.com</a>.</p>"
    ),
    "buy.email_label": "Email nhận license",
    "buy.email_hint": "Ghi đúng email này trong nội dung chuyển khoản: <code>BE {email}</code>",
    "buy.btn_qr": "Hiện QR chuyển khoản",
    "buy.btn_card": "Thanh toán thẻ",
    "buy.redirect_sepay": "Đang chuyển sang cổng thanh toán…",
    "buy.transfer_title": "Chuyển khoản — {amount} ₫",
    "buy.bank": "Ngân hàng",
    "buy.account": "Số tài khoản",
    "buy.amount": "Số tiền",
    "buy.memo": "Nội dung CK",
    "buy.wait_hint": (
        "<strong>Chưa có email ngay sau khi bấm nút.</strong> Key gửi tới <strong>{email}</strong> "
        "sau khi ngân hàng xác nhận (thường 1–10 phút). "
        "Nội dung CK phải có <code>{memo}</code>. Kiểm tra cả thư mục spam."
    ),
    "buy.success_title": "Cảm ơn bạn",
    "buy.success_with_email": (
        "License sẽ gửi tới <strong>{email}</strong> sau khi thanh toán được xác nhận, "
        "thường trong vài phút."
    ),
    "buy.success_no_email": "License sẽ gửi qua email bạn đã nhập khi thanh toán được xác nhận.",
    "buy.success_spam": "Chưa thấy mail? Đợi thêm, kiểm tra spam, đảm bảo nội dung CK đúng <code>BE {email}</code>.",
    "buy.success_download": "Tải & cài plugin",
    "page.home": "Body Exporter — add-in SolidWorks xuất Excel",
    "page.download": "Tải plugin Body Exporter",
    "page.buy": "Mua license Body Exporter",
    "page.buy_success": "Thanh toán thành công — Body Exporter",
    "error.404.title": "Không tìm thấy trang",
    "error.404.lead": "Đường dẫn không tồn tại hoặc đã đổi.",
    "error.404.home": "Trang chủ",
    "error.404.download": "Tải plugin",
    "error.500.title": "Có lỗi xảy ra",
    "error.500.lead": "Thử tải lại trang. Nếu vẫn lỗi, liên hệ hỗ trợ.",
    "error.500.home": "Trang chủ",
    "error.500.contact": "Liên hệ",
}
