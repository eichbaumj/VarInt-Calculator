from PyQt5.QtWidgets import (QApplication, QMainWindow, QWidget, QVBoxLayout, QGridLayout, 
                           QPushButton, QLineEdit, QLabel, QMenuBar, QMenu, QAction, 
                           QTextEdit, QDialog, QHBoxLayout)
from PyQt5.QtGui import QIcon, QPixmap
from PyQt5.QtCore import Qt
import sys
import time

class VarIntCalculator(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("VarInt Calculator")
        self.setFixedSize(400, 380)

        # Main widget and layout
        self.central_widget = QWidget()
        self.setCentralWidget(self.central_widget)
        self.main_layout = QVBoxLayout()
        self.central_widget.setLayout(self.main_layout)

        # Input field
        self.entry = QLineEdit()
        self.entry.setPlaceholderText("Enter value here...")
        self.entry.setAlignment(Qt.AlignRight)
        self.entry.setFixedHeight(40)
        self.main_layout.addWidget(self.entry)

        # Button grid layout
        self.button_grid = QGridLayout()
        buttons = [
            ("A", 1, 0), ("1", 1, 1), ("2", 1, 2), ("3", 1, 3),
            ("B", 2, 0), ("4", 2, 1), ("5", 2, 2), ("6", 2, 3),
            ("C", 3, 0), ("7", 3, 1), ("8", 3, 2), ("9", 3, 3),
            ("D", 4, 0), ("E", 4, 1), ("F", 4, 2), ("0", 4, 3),
            ("Clear", 5, 0), ("VarInt", 5, 1), ("Integer", 5, 2), ("Length", 5, 3)
        ]

        for text, row, col in buttons:
            button = QPushButton(text)
            button.setFixedSize(80, 50)
            if text in ["A", "B", "C", "D", "E", "F"]:
                button.setStyleSheet("background-color: #003366; color: white; font-weight: bold;")
            elif text == "Clear":
                button.setStyleSheet("background-color: #ee3d53; color: white; font-weight: bold;")
            elif text in ["VarInt", "Integer", "Length"]:
                button.setStyleSheet("background-color: #4682b4; color: white; font-weight: bold;")
            else:
                button.setStyleSheet("background-color: #d3d3d3; color: black; font-weight: bold;")
            button.clicked.connect(lambda checked, t=text: self.on_button_click(t))
            self.button_grid.addWidget(button, row, col)

        self.main_layout.addLayout(self.button_grid)

        # Status bar
        self.status_bar = QLabel("Ready")
        self.status_bar.setAlignment(Qt.AlignLeft)
        self.main_layout.addWidget(self.status_bar)

        # Menu bar
        self.menu_bar = QMenuBar(self)
        self.setMenuBar(self.menu_bar)

        # File menu
        file_menu = QMenu("File", self)
        self.menu_bar.addMenu(file_menu)
        close_action = QAction("Close", self)
        close_action.triggered.connect(self.close)
        file_menu.addAction(close_action)

        # Options menu
        options_menu = QMenu("Options", self)
        self.menu_bar.addMenu(options_menu)
        self.classic_action = QAction("Classic", self, checkable=True)
        self.classic_action.triggered.connect(self.toggle_theme)
        self.classic_action.setChecked(True)
        options_menu.addAction(self.classic_action)
        
        self.dark_mode_action = QAction("Dark Mode", self, checkable=True)
        self.dark_mode_action.triggered.connect(self.toggle_theme)
        options_menu.addAction(self.dark_mode_action)

        # View menu
        view_menu = QMenu("View", self)
        self.menu_bar.addMenu(view_menu)
        history_action = QAction("History", self)
        history_action.triggered.connect(self.toggle_history)
        view_menu.addAction(history_action)

        # Help menu
        help_menu = QMenu("Help", self)
        self.menu_bar.addMenu(help_menu)
        about_action = QAction("About", self)
        about_action.triggered.connect(self.show_about)
        help_menu.addAction(about_action)

        # History log
        self.history = []
        self.history_window = None

        # Set dark mode by default
        self.dark_mode_action.setChecked(True)
        self.classic_action.setChecked(False)
        self.apply_dark_mode()

    def on_button_click(self, text):
        if text == "Clear":
            self.entry.clear()
            self.status_bar.setText("Cleared input field")
        else:
            current_text = self.entry.text()
            self.entry.setText(current_text + text)
    
    def toggle_theme(self):
        sender = self.sender()
        self.classic_action.setChecked(sender == self.classic_action)
        self.dark_mode_action.setChecked(sender == self.dark_mode_action)
        
        if sender == self.dark_mode_action and sender.isChecked():
            self.apply_dark_mode()
        else:
            self.apply_classic_mode()
    
    def apply_dark_mode(self):
        self.central_widget.setStyleSheet("background-color: #2e2e2e; color: white;")
        self.entry.setStyleSheet("background-color: #333333; color: white;")
        for i in range(self.button_grid.count()):
            button = self.button_grid.itemAt(i).widget()
            if button.text() == "Clear":
                button.setStyleSheet("background-color: #ee3d53; color: white; font-weight: bold;")
            elif button.text() in ["A", "B", "C", "D", "E", "F"]:
                button.setStyleSheet("background-color: #003366; color: white; font-weight: bold;")
            elif button.text() in ["VarInt", "Integer", "Length"]:
                button.setStyleSheet("background-color: #4682b4; color: white; font-weight: bold;")
            else:
                button.setStyleSheet("background-color: #777777; color: black; font-weight: bold;")
    
    def apply_classic_mode(self):
        self.central_widget.setStyleSheet("")
        self.entry.setStyleSheet("")
        for i in range(self.button_grid.count()):
            button = self.button_grid.itemAt(i).widget()
            if button.text() == "Clear":
                button.setStyleSheet("background-color: #ee3d53; color: white; font-weight: bold;")
            elif button.text() in ["A", "B", "C", "D", "E", "F"]:
                button.setStyleSheet("background-color: #003366; color: white; font-weight: bold;")
            elif button.text() in ["VarInt", "Integer", "Length"]:
                button.setStyleSheet("background-color: #4682b4; color: white; font-weight: bold;")
            else:
                button.setStyleSheet("background-color: #d3d3d3; color: black; font-weight: bold;")
    
    def show_about(self):
        about_dialog = QDialog(self)
        about_dialog.setWindowTitle("About VarInt Calculator")
        about_dialog.setStyleSheet("background-color: #1B293B; color: white;")

        about_layout = QVBoxLayout()
        about_text = QLabel("VarInt Calculator v1.0")
        about_text.setAlignment(Qt.AlignCenter)
        about_layout.addWidget(about_text)

        about_dialog.setLayout(about_layout)
        about_dialog.setFixedSize(400, 200)
        about_dialog.exec_()
    
    def add_to_history(self, action):
        timestamp = time.strftime("%Y-%m-%d %H:%M:%S")
        self.history.append(f"[{timestamp}] {action}")
        self.status_bar.setText(f"History updated - {len(self.history)} entries")
        if self.history_window and self.history_window.isVisible():
            self.history_text.setPlainText("\n".join(self.history))
    
    def toggle_history(self):
        if self.history_window is None or not self.history_window.isVisible():
            self.history_window = QDialog(self)
            self.history_window.setWindowTitle("History")
            self.history_window.setGeometry(100, 100, 600, 400)
            history_layout = QVBoxLayout()
            self.history_text = QTextEdit()
            self.history_text.setReadOnly(True)
            self.history_text.setPlainText("\n".join(self.history))
            history_layout.addWidget(self.history_text)
            self.history_window.setLayout(history_layout)
            
            if self.dark_mode_action.isChecked():
                self.history_window.setStyleSheet("background-color: #2e2e2e; color: white;")
                self.history_text.setStyleSheet("background-color: #333333; color: white;")
                
            self.history_window.show()

if __name__ == "__main__":
    app = QApplication(sys.argv)
    window = VarIntCalculator()
    window.show()
    sys.exit(app.exec_()) 