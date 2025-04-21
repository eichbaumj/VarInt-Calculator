import base64
from PyQt5.QtGui import QIcon, QPixmap
from PyQt5.QtCore import QByteArray, QBuffer, QIODevice

# Simple icon data (blue square with 'VI' text)
icon_data = """
iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAAsTAAALEwEAmpwYAAAC
GElEQVRYhe2XTWsTURSGn3snk2Qyk2Q6adMmpkljbVNpa2mLCxeuXLly4c6FC3+CP0Fw5Q9w4UJE
EBERkSLiwpW0FGnVftAmJk0yadqkk5l8XKRNSdOZpJPYjS/cxcy95z3PnDmHe0dIKVmPUNZFHTAA
G0CdSLUB3E0lE0/WA4AdWDBk9ZGu6xHDMCJSSuE4jlcoFHJAWkr5APj0vwC6rofb2tqOJBKJHq/X
2xWJRLYFg8FWVVV9AF4qP1paWgJSSrW5uTkjpUS0t7d3a5qWAB77fL6nPT097bFYLNbZ2dkRCoUa
/X5/k6IoqmEYRrFYtCzLmrQsa75YLE4XCoXxXC73KZvNvpuamhoBJoAPgNTWUjIJRAFc1/UcxzEB
E5hfXFycsW17wrbtMdd188BGSvw00AoEgCZVVYOKogTgiwaXAQRzudzH4eHhR/39/Tf6+vouAjug
EJgfGLh1IZFI7PL7/c35fH768+fxx48eDb4G3EpVaoRSodPASeA8cA7YC+wA2oBGYJPbHWgGGoAd
um5c0nU9WjlpGbC8SgOoQBg4ApwGTgG7ASPgCc0GvL6WlpZY5YDPCUDUPEBIcIVzxnHsbOUvxwHH
cRwXltTrgFvjtNu2PVF5owCMWpY1UiqVzNKGW5b1w7btH8AIMAC8QYrF0gQwJIR4oes6Qoi8lDIH
pKoOfh0B/Ec+UF4c/PUN/9L4CdppLBJYv5wGAAAAAElFTkSuQmCC
"""

def create_app_icon():
    """Creates and saves the application icon"""
    icon_bytes = base64.b64decode(icon_data)
    pixmap = QPixmap()
    pixmap.loadFromData(icon_bytes)
    
    # Save as ICO for Windows
    buffer = QBuffer()
    buffer.open(QIODevice.WriteOnly)
    pixmap.save(buffer, "ICO")
    
    with open("icon.ico", "wb") as f:
        f.write(buffer.data())
    
    return QIcon(pixmap)

if __name__ == "__main__":
    # Generate the icon when run directly
    icon = create_app_icon()
    print("Icon created successfully!") 