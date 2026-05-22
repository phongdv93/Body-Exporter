"""Static legal copy for Paddle / public policy pages (vi + en)."""

from __future__ import annotations

from app import config


def _support() -> str:
    return config.SUPPORT_EMAIL


def _site() -> str:
    return config.SITE_URL.rstrip("/")


def legal_html(lang: str, page: str) -> str:
    lang = "en" if (lang or "").lower() == "en" else "vi"
    fn = _PAGES.get(page, {}).get(lang) or _PAGES.get(page, {}).get("en", "")
    return fn() if callable(fn) else ""


def legal_page_title(lang: str, page: str) -> str:
    titles = {
        "terms": ("Điều khoản dịch vụ", "Terms of Service"),
        "privacy": ("Chính sách quyền riêng tư", "Privacy Policy"),
        "refund": ("Chính sách hoàn tiền", "Refund Policy"),
    }
    vi, en = titles.get(page, ("", ""))
    return en if lang == "en" else vi


def _terms_vi() -> str:
    s, e = _site(), _support()
    return f"""
<h2>1. Giới thiệu</h2>
<p>Điều khoản này điều chỉ việc bạn sử dụng website <a href="{s}">{s}</a> và mua license phần mềm
<strong>Body Exporter</strong> (add-in SolidWorks). Bằng việc tải, cài đặt hoặc thanh toán, bạn đồng ý các điều khoản dưới đây.</p>
<h2>2. Sản phẩm</h2>
<p>Body Exporter là phần mềm bổ trợ (add-in) chạy trên Windows với SolidWorks, giúp xuất dữ liệu body/part
ra Excel và template. License cấp quyền sử dụng theo thời hạn đã mua (thường 12 tháng), gắn với máy tính
(fingerprint) sau khi kích hoạt.</p>
<h2>3. Giá và thanh toán</h2>
<p>Giá niêm yết hiển thị tại trang <a href="{s}/buy">/buy</a> (VND và mô tả quốc tế nếu có).
Thanh toán có thể qua chuyển khoản Việt Nam hoặc cổng thẻ quốc tế khi được cung cấp.
License được gửi tự động tới email bạn nhập sau khi thanh toán được xác nhận.</p>
<h2>4. Dùng thử</h2>
<p>Bản cài có thể bao gồm thời gian dùng thử (trial) như mô tả trên website; sau trial cần license hợp lệ.</p>
<h2>5. Giới hạn sử dụng</h2>
<p>Bạn không được reverse-engineer, phân phối lại license, chia sẻ key, hoặc dùng sản phẩm trái pháp luật.
Một license thương mại thường gắn một máy kích hoạt trừ khi có thỏa thuận khác bằng văn bản.</p>
<h2>6. Hỗ trợ &amp; liên hệ</h2>
<p>Hỗ trợ kỹ thuật và khiếu nại: <a href="mailto:{e}">{e}</a>.</p>
<h2>7. Thay đổi</h2>
<p>Chúng tôi có thể cập nhật điều khoản; phiên bản mới có hiệu lực khi đăng trên website.</p>
<p class="muted fine-print">Cập nhật lần cuối: 2026.</p>
"""


def _terms_en() -> str:
    s, e = _site(), _support()
    return f"""
<h2>1. Introduction</h2>
<p>These Terms of Service govern your use of <a href="{s}">{s}</a> and your purchase of a
<strong>Body Exporter</strong> license (a SolidWorks add-in for Windows). By downloading, installing, or paying,
you agree to these terms.</p>
<h2>2. Product</h2>
<p>Body Exporter exports body/part data from SolidWorks to Excel and custom templates. A license grants use for the
purchased term (typically 12 months), tied to a machine fingerprint after activation.</p>
<h2>3. Pricing &amp; payment</h2>
<p>Prices are shown on <a href="{s}/buy">/buy</a> before checkout. Payment may be via Vietnamese bank transfer or
international card where offered. Your license key is delivered to the email you provide after payment confirmation.</p>
<h2>4. Trial</h2>
<p>Installers may include a time-limited trial as described on the site; continued use requires a valid license.</p>
<h2>5. Acceptable use</h2>
<p>You may not redistribute license keys, circumvent activation, or use the software unlawfully. Unless agreed otherwise,
one commercial license is intended for one activated machine.</p>
<h2>6. Support</h2>
<p>Contact: <a href="mailto:{e}">{e}</a>.</p>
<h2>7. Changes</h2>
<p>We may update these terms; the current version is the one published on this page.</p>
<p class="muted fine-print">Last updated: 2026.</p>
"""


def _privacy_vi() -> str:
    s, e = _site(), _support()
    return f"""
<h2>1. Phạm vi</h2>
<p>Chính sách này mô tả dữ liệu thu thập qua website <a href="{s}">{s}</a>, trang tải plugin, cửa sổ license
trong phần mềm, và webhook thanh toán.</p>
<h2>2. Dữ liệu thu thập</h2>
<ul>
<li><strong>Website:</strong> cookie ngôn ngữ, cookie đồng ý chính sách tải file; lượt tải (IP, thời gian, phiên bản).</li>
<li><strong>Mua license:</strong> email, thông tin giao dịch từ ngân hàng/cổng thanh toán (số tiền, mã giao dịch).</li>
<li><strong>Plugin:</strong> fingerprint máy, hostname, phiên bản plugin/SolidWorks, IP (để license và hỗ trợ), sự kiện cài/heartbeat.</li>
</ul>
<h2>3. Mục đích</h2>
<p>Kích hoạt license, gửi key qua email, chống gian lận, cải thiện sản phẩm, hỗ trợ khách hàng, và tuân thủ kế toán.</p>
<h2>4. Chia sẻ</h2>
<p>Chúng tôi dùng nhà cung cấp email, hosting, thanh toán (ví dụ cổng quốc tế) và có thể lưu license trên hạ tầng cloud
theo cấu hình — chỉ dữ liệu cần thiết cho dịch vụ.</p>
<h2>5. Lưu trữ &amp; bảo mật</h2>
<p>Dữ liệu lưu trên máy chủ có kiểm soát truy cập; không bán dữ liệu cá nhân cho bên thứ ba marketing.</p>
<h2>6. Quyền của bạn</h2>
<p>Bạn có thể yêu cầu truy cập, sửa hoặc xóa email/dữ liệu liên quan bằng cách liên hệ
<a href="mailto:{e}">{e}</a>.</p>
<h2>7. Liên hệ</h2>
<p>Câu hỏi về quyền riêng tư: <a href="mailto:{e}">{e}</a>.</p>
<p class="muted fine-print">Cập nhật lần cuối: 2026.</p>
"""


def _privacy_en() -> str:
    s, e = _site(), _support()
    return f"""
<h2>1. Scope</h2>
<p>This Privacy Policy covers data collected through <a href="{s}">{s}</a>, the plugin download flow, in-app licensing,
and payment webhooks.</p>
<h2>2. Data we collect</h2>
<ul>
<li><strong>Website:</strong> language cookie, download-consent cookie, download events (IP, time, version).</li>
<li><strong>Purchase:</strong> email address and payment metadata from your bank or payment provider.</li>
<li><strong>Plugin:</strong> machine fingerprint, hostname, plugin/SolidWorks version, IP for licensing/support, install/heartbeat events.</li>
</ul>
<h2>3. Purposes</h2>
<p>License delivery, fraud prevention, product improvement, customer support, and legal/accounting obligations.</p>
<h2>4. Sharing</h2>
<p>We use email, hosting, and payment processors (including international checkout when enabled) and may store license
records on cloud infrastructure — only what is needed to operate the service.</p>
<h2>5. Security</h2>
<p>Access to servers is restricted; we do not sell personal data for third-party advertising.</p>
<h2>6. Your rights</h2>
<p>You may request access, correction, or deletion of your data by emailing <a href="mailto:{e}">{e}</a>.</p>
<h2>7. Contact</h2>
<p>Privacy questions: <a href="mailto:{e}">{e}</a>.</p>
<p class="muted fine-print">Last updated: 2026.</p>
"""


def _refund_vi() -> str:
    s, e = _site(), _support()
    return f"""
<h2>1. Hàng hóa số</h2>
<p>Body Exporter là phần mềm tải về và license key gửi qua email. Sau khi key được gửi và/hoặc kích hoạt thành công,
sản phẩm được coi là đã cung cấp.</p>
<h2>2. Hoàn tiền</h2>
<p>Chúng tôi xem xét hoàn tiền trong <strong>14 ngày</strong> kể từ thanh toán nếu:</p>
<ul>
<li>License chưa được kích hoạt trên bất kỳ máy nào, hoặc</li>
<li>Lỗi kỹ thuật nghiêm trọng từ phía chúng tôi khiến không thể dùng phần mềm và hỗ trợ không khắc phục được.</li>
</ul>
<p>Không hoàn tiền khi: đã kích hoạt và sử dụng bình thường, vi phạm điều khoản, hoặc yêu cầu sau 14 ngày (trừ luật bắt buộc).</p>
<h2>3. Cách yêu cầu</h2>
<p>Gửi email tới <a href="mailto:{e}">{e}</a> kèm email mua hàng, ngày thanh toán, mã giao dịch (nếu có).
Hoàn tiền về cùng phương thức thanh toán khi có thể, trong vòng 14 ngày làm việc sau khi duyệt.</p>
<h2>4. Chargeback</h2>
<p>Vui lòng liên hệ hỗ trợ trước khi khiếu nại với ngân hàng để chúng tôi xử lý nhanh.</p>
<p class="muted fine-print">Giá và điều kiện hiển thị tại <a href="{s}/buy">/buy</a>. Cập nhật: 2026.</p>
"""


def _refund_en() -> str:
    s, e = _site(), _support()
    return f"""
<h2>1. Digital product</h2>
<p>Body Exporter is downloadable software with a license key sent by email. Once the key is delivered and/or
successfully activated, the product is considered supplied.</p>
<h2>2. Refunds</h2>
<p>We may approve refunds within <strong>14 days</strong> of payment if:</p>
<ul>
<li>The license has not been activated on any machine, or</li>
<li>A critical defect on our side prevents use and support cannot resolve it.</li>
</ul>
<p>Refunds are generally not available after activation and normal use, terms violations, or requests after 14 days
(except where required by law).</p>
<h2>3. How to request</h2>
<p>Email <a href="mailto:{e}">{e}</a> with your purchase email, payment date, and transaction reference.
Approved refunds are processed to the original payment method when possible within 14 business days.</p>
<h2>4. Disputes</h2>
<p>Please contact support before opening a payment dispute so we can help quickly.</p>
<p class="muted fine-print">Pricing is shown on <a href="{s}/buy">/buy</a>. Last updated: 2026.</p>
"""


_PAGES: dict[str, dict[str, object]] = {
    "terms": {"vi": _terms_vi, "en": _terms_en},
    "privacy": {"vi": _privacy_vi, "en": _privacy_en},
    "refund": {"vi": _refund_vi, "en": _refund_en},
}
