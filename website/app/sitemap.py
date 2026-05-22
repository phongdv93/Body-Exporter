"""Public URLs for Google Search Console / sitemap.xml."""

from dataclasses import dataclass
from datetime import datetime, timezone


@dataclass(frozen=True)
class SitemapEntry:
    path: str
    changefreq: str = "weekly"
    priority: str = "0.8"


# Only indexable marketing pages (not /admin, /api, /webhook, /buy/success).
PUBLIC_SITEMAP_ENTRIES: tuple[SitemapEntry, ...] = (
    SitemapEntry("/", changefreq="weekly", priority="1.0"),
    SitemapEntry("/download", changefreq="weekly", priority="0.9"),
    SitemapEntry("/buy", changefreq="monthly", priority="0.8"),
    SitemapEntry("/terms-and-conditions", changefreq="yearly", priority="0.4"),
    SitemapEntry("/privacy", changefreq="yearly", priority="0.4"),
    SitemapEntry("/refund", changefreq="yearly", priority="0.4"),
)


def sitemap_lastmod() -> str:
    """ISO date for &lt;lastmod&gt; (UTC)."""
    return datetime.now(timezone.utc).strftime("%Y-%m-%d")


def build_sitemap_xml(site_url: str) -> str:
    base = site_url.rstrip("/")
    lines = [
        '<?xml version="1.0" encoding="UTF-8"?>',
        '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
    ]
    lastmod = sitemap_lastmod()
    for entry in PUBLIC_SITEMAP_ENTRIES:
        loc = f"{base}{entry.path}"
        lines.append("  <url>")
        lines.append(f"    <loc>{loc}</loc>")
        lines.append(f"    <lastmod>{lastmod}</lastmod>")
        lines.append(f"    <changefreq>{entry.changefreq}</changefreq>")
        lines.append(f"    <priority>{entry.priority}</priority>")
        lines.append("  </url>")
    lines.append("</urlset>")
    return "\n".join(lines)
