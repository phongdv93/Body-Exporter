"""Seed + helpers for SEO blog posts (Body Exporter)."""

from __future__ import annotations

import re
from datetime import datetime

from sqlalchemy import select
from sqlalchemy.orm import Session

from app.models import BlogPost

_SLUG_RE = re.compile(r"[^a-z0-9\-]+")


def slugify(text: str) -> str:
    t = (text or "").strip().lower()
    # Basic Vietnamese accent strip for slugs typed in admin.
    for a, b in (
        ("àáạảãâầấậẩẫăằắặẳẵ", "a"),
        ("èéẹẻẽêềếệểễ", "e"),
        ("ìíịỉĩ", "i"),
        ("òóọỏõôồốộổỗơờớợởỡ", "o"),
        ("ùúụủũưừứựửữ", "u"),
        ("ỳýỵỷỹ", "y"),
        ("đ", "d"),
    ):
        for ch in a:
            t = t.replace(ch, b)
    t = t.replace(" ", "-").replace("_", "-")
    t = _SLUG_RE.sub("-", t)
    t = re.sub(r"-{2,}", "-", t).strip("-")
    return t[:160] or "bai-viet"


def fig(slot: str, caption_vi: str, caption_en: str) -> tuple[str, str]:
    """Editable image placeholder — admin replaces with <img src=\"/uploads/blog/...\">."""
    vi = (
        f'<figure class="blog-figure" data-slot="{slot}">'
        f'<div class="blog-img-placeholder" contenteditable="false">'
        f"<strong>Chỗ gắn ảnh</strong><br>{caption_vi}<br>"
        f"<span>Admin → Bài viết → Upload ảnh → dán URL vào HTML</span>"
        f"</div>"
        f"<figcaption>{caption_vi}</figcaption>"
        f"</figure>"
    )
    en = (
        f'<figure class="blog-figure" data-slot="{slot}">'
        f'<div class="blog-img-placeholder" contenteditable="false">'
        f"<strong>Image slot</strong><br>{caption_en}<br>"
        f"<span>Admin → Articles → Upload image → paste URL into HTML</span>"
        f"</div>"
        f"<figcaption>{caption_en}</figcaption>"
        f"</figure>"
    )
    return vi, en


def localized(post: BlogPost, lang: str) -> dict:
    en = lang == "en"
    title = (post.title_en if en and post.title_en.strip() else post.title_vi) or post.title_vi
    excerpt = (post.excerpt_en if en and post.excerpt_en.strip() else post.excerpt_vi) or post.excerpt_vi
    body = (post.body_html_en if en and post.body_html_en.strip() else post.body_html_vi) or post.body_html_vi
    meta_desc = (
        (post.meta_description_en if en and post.meta_description_en.strip() else post.meta_description_vi)
        or post.meta_description_vi
        or excerpt
    )
    meta_kw = (
        (post.meta_keywords_en if en and post.meta_keywords_en.strip() else post.meta_keywords_vi)
        or post.meta_keywords_vi
    )
    return {
        "title": title.strip(),
        "excerpt": excerpt.strip(),
        "body_html": body,
        "meta_description": meta_desc.strip(),
        "meta_keywords": meta_kw.strip(),
    }


def list_published(db: Session) -> list[BlogPost]:
    return list(
        db.scalars(
            select(BlogPost)
            .where(BlogPost.published.is_(True))
            .order_by(BlogPost.sort_order.asc(), BlogPost.published_at.desc(), BlogPost.id.desc())
        ).all()
    )


def get_by_slug(db: Session, slug: str, *, published_only: bool = True) -> BlogPost | None:
    q = select(BlogPost).where(BlogPost.slug == slug.strip())
    post = db.scalar(q)
    if not post:
        return None
    if published_only and not post.published:
        return None
    return post


def _seed_defs() -> list[dict]:
    f1_vi, f1_en = fig(
        "bom-grid",
        "Ảnh gợi ý: cửa sổ Body Exporter — cột Type (Detail/Hardware/Packaging), kích thước L×W×T.",
        "Suggested: Body Exporter window — Type tags, L×W×T columns.",
    )
    f2_vi, f2_en = fig(
        "excel-sheets",
        "Ảnh gợi ý: file Excel xuất ra — sheet theo Type hoặc template công ty đã fill.",
        "Suggested: exported Excel — sheets by Type or filled company template.",
    )
    f3_vi, f3_en = fig(
        "erp-push",
        "Ảnh gợi ý: dialog Send to ERP / product code trên plugin.",
        "Suggested: Send to ERP dialog with product code.",
    )
    f4_vi, f4_en = fig(
        "install",
        "Ảnh gợi ý: Tools → Add-Ins trong SolidWorks với Body Exporter đã bật.",
        "Suggested: SolidWorks Tools → Add-Ins with Body Exporter enabled.",
    )

    return [
        {
            "slug": "xuat-bom-tu-solidworks",
            "sort_order": 10,
            "title_vi": "Xuất BOM từ SolidWorks: từ model đa body đến bảng sản xuất",
            "title_en": "Export a BOM from SolidWorks: from multibody models to the shop floor",
            "excerpt_vi": (
                "Cách lấy danh sách chi tiết (BOM) từ file Part đa body trong SolidWorks — "
                "không copy tay, không quên Quantity — bằng Body Exporter."
            ),
            "excerpt_en": (
                "How to pull a usable bill of materials from SolidWorks multibody parts — "
                "without hand-copying dimensions — using Body Exporter."
            ),
            "meta_keywords_vi": (
                "xuất BOM từ SolidWorks, xuất danh sách chi tiết từ SolidWorks, BOM export SolidWorks, "
                "Body Exporter, add-in SolidWorks, xưởng mộc"
            ),
            "meta_keywords_en": (
                "export BOM from SolidWorks, SolidWorks BOM export, multibody BOM, "
                "Body Exporter, SolidWorks add-in, cutting list"
            ),
            "meta_description_vi": (
                "Hướng dẫn xuất BOM / danh sách chi tiết từ SolidWorks Part đa body sang Excel "
                "với Body Exporter — Type, Quantity, L×W×T cho sản xuất."
            ),
            "meta_description_en": (
                "Guide: export a BOM / detail list from SolidWorks multibody parts to Excel "
                "with Body Exporter — Type, Quantity, and L×W×T for production."
            ),
            "body_html_vi": f"""
<p>Nhiều xưởng dùng SolidWorks để thiết kế tủ, bàn, vách — nhưng khâu <strong>xuất BOM từ SolidWorks</strong>
vẫn là copy tay từ FeatureManager sang Excel. Sai một dòng Quantity hoặc đảo Length/Width là lệnh cắt lệch.</p>
<p><strong>Body Exporter</strong> là add-in đọc trực tiếp các solid body trong file <code>.SLDPRT</code>,
gộp body giống nhau thành một dòng kèm số lượng, rồi xuất Excel / template / (tuỳ cấu hình) đẩy ERP.</p>
{f1_vi}
<h2>BOM “đủ dùng sản xuất” cần gì?</h2>
<ul>
<li><strong>Tên chi tiết</strong> — tên body hoặc tên bạn đặt lại trong plugin.</li>
<li><strong>Kích thước L × W × T (mm)</strong> — map trục X/Y/Z theo thói quen xưởng.</li>
<li><strong>Quantity</strong> — số body giống nhau (pattern, mirror, copy).</li>
<li><strong>Type / nhóm</strong> — Detail (gỗ), Hardware (vật tư), Packaging (bao bì), Other.</li>
<li><strong>Vật liệu &amp; appearance</strong> — snapshot từ SolidWorks khi có.</li>
</ul>
<h2>Quy trình 5 bước</h2>
<ol>
<li>Mở Part đa body trong SolidWorks.</li>
<li>Mở Body Exporter (shortcut Desktop hoặc Tools → Add-Ins).</li>
<li>Chỉnh tên hiển thị, trục L/W/T, gán Type (hoặc để keyword tự gán).</li>
<li>Export → New Excel workbook hoặc Fill Excel template.</li>
<li>Save trong plugin để giữ metadata trong file Part.</li>
</ol>
{f2_vi}
<h2>Khác gì BOM mặc định của SolidWorks?</h2>
<p>BOM assembly của SolidWorks mạnh ở cấu trúc lắp ráp. Với <em>một Part nhiều body</em> (workflow phổ biến ở xưởng mộc),
bạn cần danh sách body + kích thước bounding box theo trục sản xuất — đúng việc Body Exporter làm.</p>
<p class="blog-cta">Dùng thử 14 ngày: <a href="/download">Tải plugin</a> · <a href="/buy">Mua license</a></p>
""",
            "body_html_en": f"""
<p>Many shops design furniture in SolidWorks, then still <strong>export the BOM by hand</strong>
from the FeatureManager into Excel. One wrong Quantity or swapped Length/Width and the cut list is wrong.</p>
<p><strong>Body Exporter</strong> reads solid bodies in a <code>.SLDPRT</code>, merges identical bodies into
one row with Quantity, then exports Excel / a company template / (optionally) pushes lines to ERP.</p>
{f1_en}
<h2>What a production-ready BOM needs</h2>
<ul>
<li><strong>Part / body name</strong></li>
<li><strong>L × W × T (mm)</strong> with axis mapping that matches the shop</li>
<li><strong>Quantity</strong> for mirrored / patterned copies</li>
<li><strong>Type</strong> — Detail, Hardware, Packaging, Other</li>
<li><strong>Material &amp; appearance</strong> when available</li>
</ul>
<h2>Five-step workflow</h2>
<ol>
<li>Open a multibody Part.</li>
<li>Launch Body Exporter.</li>
<li>Rename rows, set L/W/T axes, assign Type (or keyword auto-type).</li>
<li>Export → New workbook or Fill Excel template.</li>
<li>Save in the plugin to persist metadata in the Part file.</li>
</ol>
{f2_en}
<p class="blog-cta">14-day trial: <a href="/download">Download</a> · <a href="/buy">Buy license</a></p>
""",
        },
        {
            "slug": "xuat-body-solidworks-sang-excel",
            "sort_order": 20,
            "title_vi": "Xuất body SolidWorks sang Excel — giữ đúng L×W×T cho xưởng",
            "title_en": "Export SolidWorks bodies to Excel — keep L×W×T correct for production",
            "excerpt_vi": (
                "Hướng dẫn xuất từng body trong Part SolidWorks ra Excel: preview, kích thước, "
                "template {{placeholder}}, và lịch sử file gần đây."
            ),
            "excerpt_en": (
                "Export each body from a SolidWorks Part to Excel: previews, dimensions, "
                "{{placeholder}} templates, and recent export history."
            ),
            "meta_keywords_vi": (
                "xuất body từ SolidWorks, xuất body SolidWorks Excel, body export, "
                "SolidWorks Excel export, template Excel SolidWorks, Body Exporter"
            ),
            "meta_keywords_en": (
                "export body from SolidWorks, SolidWorks body to Excel, body export add-in, "
                "Excel template SolidWorks, Body Exporter"
            ),
            "meta_description_vi": (
                "Cách xuất body từ SolidWorks sang Excel với Body Exporter: workbook mới, "
                "fill template công ty, ảnh preview và lịch sử 8 lần export gần nhất."
            ),
            "meta_description_en": (
                "How to export SolidWorks bodies to Excel with Body Exporter: new workbook, "
                "company template fill, preview images, and recent export history."
            ),
            "body_html_vi": f"""
<p>Khi Part có hàng chục body, nhu cầu không chỉ là “có bảng” — mà là <strong>xuất body từ SolidWorks</strong>
sang Excel đúng cột mà kế hoạch sản xuất / nesting đang dùng.</p>
{f2_vi}
<h2>Hai kiểu export Excel</h2>
<ol>
<li><strong>New Excel workbook</strong> — tạo file mới, tách sheet theo Type (Detail, Hardware…).</li>
<li><strong>Fill Excel template</strong> — clone file mẫu công ty; ô có <code>{{{{Body Name}}}}</code>,
<code>{{{{Length}}}}</code>, <code>{{{{Type}}}}</code>, <code>{{{{Preview}}}}</code>… được điền tự động.</li>
</ol>
<p>Template giữ nguyên logo, merge cell và công thức — plugin chỉ ghi giá trị vào ô placeholder.</p>
<h2>Preview ảnh trong Excel</h2>
<p>Bật “Include preview images” trước khi export nếu cần ảnh thumbnail từng dòng (hữu ích khi QC hoặc gửi khách).</p>
<h2>Recent Excel exports</h2>
<p>Menu Export → <em>Recent Excel exports…</em> mở panel tối đa 8 file gần nhất (New workbook hoặc Fill template),
mỗi dòng có nút Open — khỏi lần mò thư mục Downloads.</p>
{f1_vi}
<p class="blog-cta"><a href="/download">Tải Body Exporter</a> và thử trên một Part đang làm.</p>
""",
            "body_html_en": f"""
<p>With dozens of bodies in one Part, you need more than “a table” — you need a reliable
<strong>SolidWorks body → Excel</strong> pipeline that matches how production already works.</p>
{f2_en}
<h2>Two Excel export modes</h2>
<ol>
<li><strong>New Excel workbook</strong> — sheets split by Type.</li>
<li><strong>Fill Excel template</strong> — clone your company .xlsx; cells with
<code>{{{{Body Name}}}}</code>, <code>{{{{Length}}}}</code>, <code>{{{{Type}}}}</code>, <code>{{{{Preview}}}}</code>
are filled automatically.</li>
</ol>
<p>Templates keep logos, merges, and formulas — the add-in only writes values into placeholder cells.</p>
{f1_en}
<p class="blog-cta"><a href="/download">Download Body Exporter</a> and try it on a live Part.</p>
""",
        },
        {
            "slug": "danh-sach-chi-tiet-xuong-moc-solidworks",
            "sort_order": 30,
            "title_vi": "Danh sách chi tiết xưởng mộc từ SolidWorks (không bắt buộc SWOOD)",
            "title_en": "Wood shop cutting lists from SolidWorks (without requiring SWOOD)",
            "excerpt_vi": (
                "Xưởng mộc / nội thất dùng SolidWorks thuần vẫn lấy được danh sách chi tiết, "
                "hardware và bao bì — bằng Body Exporter + Type + keyword."
            ),
            "excerpt_en": (
                "Furniture and millwork shops on plain SolidWorks can still produce cutting lists, "
                "hardware and packaging rows — with Body Exporter Types and keywords."
            ),
            "meta_keywords_vi": (
                "danh sách chi tiết SolidWorks, xưởng mộc SolidWorks, nesting, xuất BOM gỗ, "
                "cutting list SolidWorks, Body Exporter, vật tư bao bì"
            ),
            "meta_keywords_en": (
                "SolidWorks cutting list, woodworking BOM, furniture BOM SolidWorks, nesting, "
                "hardware packaging list, Body Exporter"
            ),
            "meta_description_vi": (
                "Lấy danh sách chi tiết / cutting list cho xưởng mộc từ SolidWorks: Type Detail–Hardware–Packaging, "
                "keyword tự gán, sắp xếp BOM trước khi xuất."
            ),
            "meta_description_en": (
                "Build wood-shop cutting lists from SolidWorks: Detail/Hardware/Packaging types, "
                "keyword auto-assign, and BOM sort before export."
            ),
            "body_html_vi": f"""
<p>SWOOD / WOODEXPERT rất mạnh — nhưng không phải xưởng nào cũng cần full suite. Nhiều team đã model bằng
SolidWorks “thuần” và chỉ thiếu khâu <strong>danh sách chi tiết</strong> ổn định.</p>
{f1_vi}
<h2>Type theo ngôn ngữ xưởng</h2>
<ul>
<li><strong>Detail</strong> — tấm / chi tiết gỗ (ERP section thường là <code>wood</code>).</li>
<li><strong>Hardware</strong> — vật tư, phụ kiện.</li>
<li><strong>Packaging</strong> — bao bì.</li>
<li><strong>Other</strong> — bỏ qua Excel/ERP mặc định (bật lại trong BOM Settings nếu cần).</li>
</ul>
<p>Trong BOM Settings bạn đổi tên EN/VI, thêm type riêng, và nhập <strong>keywords</strong>
(ví dụ <em>banle, ray, carton</em>) để body mới tự vào đúng nhóm.</p>
<h2>Sắp xếp trước khi xuất</h2>
<p>BOM sort theo keyword tier giúp Excel ra đúng thứ tự cắt / gia công thay vì thứ tự FeatureManager.</p>
{f2_vi}
<p>Kết quả: một Part → một bảng sẵn gửi tổ cắt hoặc import nesting — vẫn nằm trong SolidWorks quen thuộc.</p>
<p class="blog-cta"><a href="/buy">Mua license</a> · hỗ trợ: hotro@bodyexporter.com</p>
""",
            "body_html_en": f"""
<p>SWOOD and similar suites are powerful — but many shops already model in plain SolidWorks and only need a
<strong>reliable cutting / detail list</strong>.</p>
{f1_en}
<h2>Types that match the shop floor</h2>
<ul>
<li><strong>Detail</strong> — wood parts (ERP section often <code>wood</code>)</li>
<li><strong>Hardware</strong></li>
<li><strong>Packaging</strong></li>
<li><strong>Other</strong> — skipped from Excel/ERP by default</li>
</ul>
<p>Rename types, add custom ones, and set <strong>keywords</strong> so new bodies auto-map.</p>
{f2_en}
<p class="blog-cta"><a href="/buy">Buy a license</a> · support via the site contact email</p>
""",
        },
        {
            "slug": "day-bom-solidworks-len-erp",
            "sort_order": 40,
            "title_vi": "Đẩy BOM SolidWorks lên ERP: Type, section và quy trình thực tế",
            "title_en": "Push a SolidWorks BOM to ERP: Types, sections, and a practical flow",
            "excerpt_vi": (
                "Cách dùng Body Exporter để gửi dòng BOM từ SolidWorks sang ERP qua API key — "
                "map Type → section, kích thước tổng thể sản phẩm."
            ),
            "excerpt_en": (
                "How Body Exporter pushes BOM lines from SolidWorks to ERP via API key — "
                "Type → section mapping and overall product size."
            ),
            "meta_keywords_vi": (
                "SolidWorks ERP BOM, đẩy BOM ERP, tích hợp CAD ERP, Body Exporter ERP, "
                "API BOM SolidWorks"
            ),
            "meta_keywords_en": (
                "SolidWorks ERP BOM, CAD ERP integration, push BOM to ERP, Body Exporter API"
            ),
            "meta_description_vi": (
                "Tích hợp Body Exporter với ERP: cấu hình Base URL + API key, Send to ERP, "
                "map Type sang section (wood/hardware/packaging)."
            ),
            "meta_description_en": (
                "Connect Body Exporter to ERP: Base URL + API key, Send to ERP, "
                "and map Types to sections (wood/hardware/packaging)."
            ),
            "body_html_vi": f"""
<p>Khi Excel chỉ là bước trung gian, bước tiếp theo là <strong>đẩy BOM SolidWorks lên ERP</strong>
để kế hoạch / kho nhận đúng mã chi tiết.</p>
{f3_vi}
<h2>Cấu hình một lần</h2>
<ol>
<li>Export → BOM Settings → ERP connection.</li>
<li>Base URL = origin site ERP (ví dụ <code>https://erp.example.com</code>).</li>
<li>API key từ trang CAD connection của ERP.</li>
<li>Test connection → Save.</li>
</ol>
<h2>Map Type → section</h2>
<p>Mỗi Type trong plugin có <em>ERP section</em> (mặc định: Detail→wood, Hardware→hardware, Packaging→packaging).
Có thể sửa trong BOM type settings; material đi field riêng, không nhầm với section.</p>
<h2>Payload gửi đi</h2>
<p>Mỗi dòng gồm partCode, partName, section, material, qty, length/width/thickness mm, remark;
kèm kích thước tổng thể sản phẩm (product L/W/H) khi có.</p>
{f1_vi}
<p class="blog-cta">Cần hỗ trợ nối ERP: ghi chú khi <a href="/buy">mua license</a> hoặc email hotro@bodyexporter.com</p>
""",
            "body_html_en": f"""
<p>When Excel is only a bridge, the next step is <strong>pushing the SolidWorks BOM into ERP</strong>.</p>
{f3_en}
<h2>One-time setup</h2>
<ol>
<li>Export → BOM Settings → ERP connection.</li>
<li>Base URL = ERP site origin.</li>
<li>API key from the ERP CAD connection screen.</li>
<li>Test connection → Save.</li>
</ol>
<h2>Type → section</h2>
<p>Each Type has an ERP section (defaults: Detail→wood, Hardware→hardware, Packaging→packaging).
Material stays in its own field.</p>
{f1_en}
<p class="blog-cta"><a href="/buy">Buy a license</a> if you need help wiring your ERP.</p>
""",
        },
        {
            "slug": "cai-dat-body-exporter-solidworks",
            "sort_order": 50,
            "title_vi": "Cài đặt Body Exporter trên SolidWorks + trial 14 ngày",
            "title_en": "Install Body Exporter in SolidWorks + 14-day trial",
            "excerpt_vi": (
                "Hướng dẫn cài add-in Body Exporter: đóng SolidWorks, chạy Install-BodyExporter.cmd "
                "Run as administrator, bật Add-Ins, kích hoạt trial."
            ),
            "excerpt_en": (
                "Install the Body Exporter add-in: close SolidWorks, run Install-BodyExporter.cmd "
                "as Administrator, enable Add-Ins, start the trial."
            ),
            "meta_keywords_vi": (
                "cài đặt Body Exporter, SolidWorks add-in, Install-BodyExporter, trial SolidWorks plugin"
            ),
            "meta_keywords_en": (
                "install Body Exporter, SolidWorks add-in install, SolidWorks plugin trial"
            ),
            "meta_description_vi": (
                "Cài Body Exporter cho SolidWorks Windows: ZIP, Install-BodyExporter.cmd (Admin), "
                "Tools → Add-Ins, trial 14 ngày online."
            ),
            "meta_description_en": (
                "Install Body Exporter for SolidWorks on Windows: ZIP, Install-BodyExporter.cmd (Admin), "
                "Tools → Add-Ins, 14-day online trial."
            ),
            "body_html_vi": f"""
<ol>
<li><strong>Đóng SolidWorks</strong> hoàn toàn (tránh DLL bị khóa).</li>
<li>Tải ZIP tại <a href="/download">/download</a> (đồng ý chính sách dữ liệu).</li>
<li>Giải nén → chuột phải <code>Install-BodyExporter.cmd</code> → <strong>Run as administrator</strong>.</li>
<li>Mở SolidWorks → <strong>Tools → Add-Ins</strong> → bật <em>SolidWorks Body Exporter</em>.</li>
<li>Mở Part → chạy Body Exporter → License → trial 14 ngày (cần internet lần đầu).</li>
</ol>
{f4_vi}
<p>License trả phí nhận qua email sau thanh toán tại <a href="/buy">/buy</a> (Paddle quốc tế / VietQR khi bật).</p>
<p class="blog-cta"><a href="/download">Tải ngay</a></p>
""",
            "body_html_en": f"""
<ol>
<li><strong>Quit SolidWorks</strong> completely.</li>
<li>Download the ZIP from <a href="/download">/download</a>.</li>
<li>Extract → right-click <code>Install-BodyExporter.cmd</code> → <strong>Run as administrator</strong>.</li>
<li>SolidWorks → <strong>Tools → Add-Ins</strong> → enable <em>SolidWorks Body Exporter</em>.</li>
<li>Open a Part → Body Exporter → License → 14-day trial (internet required once).</li>
</ol>
{f4_en}
<p class="blog-cta"><a href="/download">Download now</a></p>
""",
        },
    ]


def ensure_seed_posts(db: Session) -> int:
    """Insert default SEO articles if table empty. Returns number inserted."""
    existing = db.scalar(select(BlogPost.id).limit(1))
    if existing is not None:
        return 0

    now = datetime.utcnow()
    n = 0
    for raw in _seed_defs():
        db.add(
            BlogPost(
                slug=raw["slug"],
                title_vi=raw["title_vi"],
                title_en=raw["title_en"],
                excerpt_vi=raw["excerpt_vi"],
                excerpt_en=raw["excerpt_en"],
                body_html_vi=raw["body_html_vi"].strip(),
                body_html_en=raw["body_html_en"].strip(),
                meta_description_vi=raw["meta_description_vi"],
                meta_description_en=raw["meta_description_en"],
                meta_keywords_vi=raw["meta_keywords_vi"],
                meta_keywords_en=raw["meta_keywords_en"],
                cover_image="",
                published=True,
                published_at=now,
                sort_order=raw["sort_order"],
                author_name="Body Exporter",
            )
        )
        n += 1
    db.commit()
    return n
