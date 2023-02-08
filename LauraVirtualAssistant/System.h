#ifndef LAURA_SYSTEM_H
#define LAURA_SYSTEM_H

class System
{
public:
	System();

	// Deleting copy constructors
	System(const System&) = delete;
	System& operator=(const System&) = delete;

	inline std::wstring& GetCurrentUserName() { return m_UserName; }
	inline std::wstring& GetCurrentUserProfile() { return m_UserProfile; }
	inline std::wstring  GetCurrentUserDownloads() { return m_UserProfile + L"\\Downloads"; }

private:
	std::wstring m_UserName;
	std::wstring m_UserProfile;
};

#endif // !LAURA_SYSTEM_H
