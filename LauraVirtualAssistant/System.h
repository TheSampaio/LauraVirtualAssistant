#pragma once

class System
{
public:
	System();

	// Delete copy contructor and assigment operator
	System(const System&) = delete;
	System& operator=(const System&) = delete;

	// Get methods
	inline std::wstring GetCurrentUserName()      { return m_UserName; }
	inline std::wstring GetCurrentUserProfile()   { return m_UserProfile; }
	inline std::wstring GetCurrentUserDownloads() { return m_UserProfile + L"\\Downloads"; }

	std::wstring GetCurrentDate();
	std::wstring GetDayPeriod();

private:
	// Attributes
	std::wstring m_UserName;
	std::wstring m_UserProfile;
};
