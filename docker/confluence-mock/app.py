import json
import pathlib
from typing import Optional
from fastapi import FastAPI, Query, Header
from fastapi.responses import HTMLResponse

app = FastAPI(title="Confluence Mock", version="1.0.0")

# Load pages.json at startup
PAGES_PATH = pathlib.Path(__file__).parent / "pages.json"
PAGES = json.loads(PAGES_PATH.read_text())
PAGES_BY_ID = {page["id"]: page for page in PAGES}


@app.get("/health")
async def health():
    """Health check endpoint."""
    return {"status": "ok"}


@app.get("/rest/api/space")
async def get_spaces(authorization: Optional[str] = Header(None)):
    """Get list of spaces (basic auth accepted but not validated)."""
    return {
        "results": [
            {
                "key": "MDM",
                "name": "Master Data Management"
            }
        ],
        "size": 1,
        "limit": 50,
        "start": 0
    }


@app.get("/rest/api/content")
async def get_content(
    spaceKey: str = Query(...),
    expand: Optional[str] = Query(None),
    start: int = Query(0),
    limit: int = Query(25),
    authorization: Optional[str] = Header(None)
):
    """Get paginated page listing from a space."""
    if spaceKey != "MDM":
        return {
            "results": [],
            "size": 0,
            "limit": limit,
            "start": start,
            "_links": {}
        }

    # Get all pages and paginate
    all_pages = PAGES

    # Convert to Confluence content format
    results = []
    for page in all_pages[start:start + limit]:
        results.append({
            "id": str(page["id"]),
            "title": page["title"],
            "version": {
                "when": page["version_when"]
            },
            "body": {
                "storage": {
                    "value": page["html_body"]
                }
            },
            "_links": {
                "webui": f"http://localhost:8090/wiki/spaces/MDM/pages/{page['id']}"
            }
        })

    # Calculate size for this batch
    remaining = len(all_pages) - start
    size = min(len(results), remaining)

    return {
        "results": results,
        "size": size,
        "limit": limit,
        "start": start,
        "_links": {}
    }


@app.get("/wiki/spaces/MDM/pages/{page_id}", response_class=HTMLResponse)
async def get_rendered_page(page_id: int, authorization: Optional[str] = Header(None)):
    """Get a rendered HTML page."""
    if page_id not in PAGES_BY_ID:
        return HTMLResponse("<html><body><h1>404 Not Found</h1></body></html>", status_code=404)

    page = PAGES_BY_ID[page_id]
    html_content = f"""<!DOCTYPE html>
<html>
<head>
    <title>{page['title']} - MDM</title>
    <meta charset="UTF-8">
</head>
<body>
    <h1>{page['title']}</h1>
    {page['html_body']}
</body>
</html>
"""
    return HTMLResponse(html_content)
