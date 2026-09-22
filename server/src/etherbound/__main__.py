import uvicorn

from etherbound.app import app
from etherbound.config import get_settings

if __name__ == "__main__":
    settings = get_settings()
    uvicorn.run(app, host=settings.host, port=settings.port)
