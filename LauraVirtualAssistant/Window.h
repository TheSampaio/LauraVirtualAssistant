#ifndef LAURA_WINDOW_H
#define LAURA_WINDOW_H

class Window
{
public:
	enum EDisplayMode
	{
		BORDLESS = 0,
		FULLSCREEN,
		WINDOWED
	};

	Window();

	// Main methods
	bool Create();
	inline void Close() { PostMessage(m_Id, WM_DESTROY, NULL, NULL); }

	// Input methods
	inline bool GetKeyPressed(int KeyCode)		 { return s_Keys[KeyCode]; }
	inline bool GetKeyReleased(int KeyCode)		 { return !s_Keys[KeyCode]; }
	inline std::array<int, 2> GetMousePosition() { return s_Mouse; }

	// Get methods
	inline HWND GetId()	const								{ return m_Id; }
	inline HINSTANCE GetInstance() const					{ return m_Instance; }
	inline COLORREF GetBackgroundColor() const				{ return m_BackgroundColor; }
	inline std::string GetTitle() const						{ return m_Title; }
	inline std::array<unsigned int, 2> GetScreen() const	{ return m_Screen; }
	inline std::array<unsigned int, 2> GetSize() const		{ return m_Size; }
	inline std::array<unsigned int, 2> GetPosition() const	{ return m_Position; }
	inline std::array<unsigned int, 2> GetCenter() const	{ return m_Center; }
	inline unsigned int GetDisplayMode() const				{ return m_DisplayMode; }

	// Set methods
	void SetSize(unsigned int Width, unsigned int Height);
	void SetDisplayMode(unsigned int DisplayMode);

	inline void SetTitle(const std::string Title)	 { m_Title = Title; }
	inline void SetIcon(const unsigned int Icon)	 { m_Icon = LoadIcon(GetModuleHandle(NULL), MAKEINTRESOURCE(Icon)); };
	inline void SetCursor(const unsigned int Cursor) { m_Cursor = LoadCursor(GetModuleHandle(NULL), MAKEINTRESOURCE(Cursor)); };
	inline void SetBackgroundColor(unsigned int Red, unsigned int Green, unsigned int Blue) { m_BackgroundColor = RGB(Red, Green, Blue); }

	// Static methods
	static LRESULT CALLBACK Procedure(HWND hWindow, UINT uMessage, WPARAM wParam, LPARAM lParam); // Window's procedure function

private:
	// Attributes
	HWND m_Id;
	HINSTANCE m_Instance;

	HICON m_Icon;
	HCURSOR m_Cursor;
	COLORREF m_BackgroundColor;
	DWORD m_Style;

	std::string m_Title;
	std::array<unsigned int, 2> m_Screen;
	std::array<unsigned int, 2> m_Size;
	std::array<unsigned int, 2> m_Position;
	std::array<unsigned int, 2> m_Center;

	unsigned int m_DisplayMode;

	static bool s_bIsHidden;
	static bool s_Keys[256];
	static std::array<int, 2> s_Mouse;
};

#endif // !LAURA_WINDOW_H
