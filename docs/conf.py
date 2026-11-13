# Sphinx configuration for the documentation site on Read the Docs. The pages are the same Markdown files GitHub
# shows, read with MyST; the hooks below adapt the few GitHub-only constructs they use.
# https://www.sphinx-doc.org/en/master/usage/configuration.html

import os
import re

project = "Fetchshelf"
author = "Fetchshelf contributors"
copyright = "Fetchshelf contributors"

# On Read the Docs, the branch or tag being built ("main", "v2026.11.8"); locally, main.
git_ref = os.environ.get("READTHEDOCS_GIT_IDENTIFIER", "main")
release = os.environ.get("READTHEDOCS_VERSION_NAME", git_ref)
repository = "https://github.com/crimsonking60/fetchshelf"

extensions = ["myst_parser"]
source_suffix = {".md": "markdown"}
root_doc = "index"
exclude_patterns = ["_build"]

# GitHub-style anchors for headings, so links such as configuration.md#follow keep working.
myst_heading_anchors = 4
myst_enable_extensions = ["colon_fence"]

html_theme = "furo"
html_title = "Fetchshelf"
html_static_path = ["images"]
html_theme_options = {
    "light_logo": "logo-light.svg",
    "dark_logo": "logo-dark.svg",
    "source_repository": repository,
    "source_branch": git_ref,
    "source_directory": "docs/",
}

# Pages may be wrapped in <!-- github-only:start --> ... <!-- github-only:end --> for text that only makes sense
# on GitHub, such as the note that main may describe unreleased changes: the site has a version selector instead.
_GITHUB_ONLY = re.compile(r"<!-- github-only:start -->.*?<!-- github-only:end -->\n?", re.DOTALL)

# GitHub's alert syntax: "> [!NOTE]" followed by quoted lines. MyST reads it as a plain quote.
_ALERT = re.compile(r"^> \[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\n((?:>.*\n?)*)", re.MULTILINE)

# Links to files outside docs/ (RELEASES.md, SECURITY.md, LICENSE, .github/...) aren't part of the site.
_OUTSIDE_LINK = re.compile(r"\]\(\.\./([^)#\s]+)(#[^)\s]*)?\)")


def _alert_to_admonition(match):
    kind = match.group(1).lower()
    body = re.sub(r"^> ?", "", match.group(2), flags=re.MULTILINE)
    return f":::{{{kind}}}\n{body.rstrip()}\n:::\n"


def _adapt_github_markdown(app, docname, source):
    text = source[0]
    text = _GITHUB_ONLY.sub("", text)
    text = _ALERT.sub(_alert_to_admonition, text)
    if "/" not in docname:
        text = _OUTSIDE_LINK.sub(
            lambda m: f"]({repository}/blob/{git_ref}/{m.group(1)}{m.group(2) or ''})", text)
    source[0] = text


def setup(app):
    app.connect("source-read", _adapt_github_markdown)
