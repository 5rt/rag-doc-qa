import { SITE_URL } from './siteUrl';

type SeoProps = {
  title: string;
  description: string;
  path: string;
  image?: string;
  noIndex?: boolean;
};

// React 19 hoists title/meta/link to <head> automatically, so no helmet needed.
// Note: this is client-side. Googlebot renders JS and sees it; social crawlers
// like LinkedIn and Slack do not, and only read the static tags in index.html.
export default function Seo({
  title,
  description,
  path,
  image = '/og-default.png',
  noIndex = false,
}: SeoProps) {
  const canonical = new URL(path, SITE_URL).toString();
  const imageUrl = new URL(image, SITE_URL).toString();
  const fullTitle = `${title} · RAG Document Q&A`;

  return (
    <>
      <title>{fullTitle}</title>
      <meta name="description" content={description} />
      <link rel="canonical" href={canonical} />
      <meta name="robots" content={noIndex ? 'noindex, nofollow' : 'index, follow'} />
      <meta property="og:type" content="website" />
      <meta property="og:url" content={canonical} />
      <meta property="og:title" content={fullTitle} />
      <meta property="og:description" content={description} />
      <meta property="og:image" content={imageUrl} />
      <meta name="twitter:card" content="summary_large_image" />
      <meta name="twitter:title" content={fullTitle} />
      <meta name="twitter:description" content={description} />
      <meta name="twitter:image" content={imageUrl} />
    </>
  );
}

