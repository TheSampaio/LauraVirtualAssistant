#pragma once

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

	// Delete copy contructor and assigment operator
	Window(const Window&) = delete;
	Window& operator=(const Window&) = delete;

	// Main methods
	bool Create();
	inline void Close() { PostMessage(m_Id, WM_DESTROY, NULL, NULL); }

	// Get methods
	inline HWND GetId()	const								{ return m_Id; }
	inline HINSTANCE GetInstance() const					{ return m_Instance; }
	inline COLORREF GetBackgroundColor() const				{ return m_BackgroundColor; }
	inline std::string GetTitle() const						{ return m_Title; }
	inline std::array<unsigned, 2> GetScreen() const	{ return m_Screen; }
	inline std::array<unsigned, 2> GetSize() const		{ return m_Size; }
	inline std::array<unsigned, 2> GetPosition() const	{ return m_Position; }
	inline std::array<unsigned, 2> GetCenter() const	{ return m_Center; }
	inline unsigned GetDisplayMode() const				{ return m_DisplayMode; }

	// Set methods
	void SetSize(unsigned Width, unsigned Height);
	void SetDisplayMode(unsigned DisplayMode);

	inline void SetTitle(const std::string Title)	                                    { m_Title = Title; }
	inline void SetIcon(const unsigned Icon)	                                        { m_Icon = LoadIcon(GetModuleHandle(NULL), MAKEINTRESOURCE(Icon)); };
	inline void SetCursor(const unsigned Cursor)                                        { m_Cursor = LoadCursor(GetModuleHandle(NULL), MAKEINTRESOURCE(Cursor)); };
	inline void SetBackgroundColor(unsigned Red, unsigned Green, unsigned Blue) { m_BackgroundColor = RGB(Red, Green, Blue); }

private:
	// Attributes
	HWND m_Id;
	HINSTANCE m_Instance;

	HICON m_Icon;
	HCURSOR m_Cursor;
	COLORREF m_BackgroundColor;
	DWORD m_Style;

	std::string m_Title;
	std::array<unsigned, 2> m_Screen;
	std::array<unsigned, 2> m_Size;
	std::array<unsigned, 2> m_Position;
	std::array<unsigned, 2> m_Center;

	unsigned m_DisplayMode;
};
