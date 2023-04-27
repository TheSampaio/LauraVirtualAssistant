#include "PCH.h"
#include "System.h"

#include "Debug.h"

System::System()
{
	// Get user's name and user's folder path (User profile)
	TCHAR UserProfilePath[MAX_PATH] {};
	std::vector<TCHAR> UserProfileBuffer;

	if SUCCEEDED(SHGetFolderPath(NULL, CSIDL_PROFILE, NULL, 0, UserProfilePath))
	{
		for (unsigned short i = 0; (UserProfilePath[i] != '\0'); i++)
		{
			if (i > 8)
				UserProfileBuffer.push_back(UserProfilePath[i]);
		}

		m_UserName = std::wstring(UserProfileBuffer.begin(), UserProfileBuffer.end());
		m_UserProfile = std::wstring(UserProfilePath);
	}
}

std::wstring System::GetCurrentDate()
{
	// Get current date in "DD-MM-YY" format
	std::chrono::local_time LocalTime = std::chrono::current_zone()->to_local(std::chrono::system_clock::now());
	std::string LocalDate = std::format("{:%d-%m-%Y}", LocalTime);

	return std::wstring(LocalDate.begin(), LocalDate.end());
}

std::wstring System::GetDayPeriod()
{
	

	return std::wstring();
}
