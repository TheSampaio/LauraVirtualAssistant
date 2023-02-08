#include "PCH.h"
#include "System.h"

System::System()
{
	TCHAR UserProfilePath[MAX_PATH] {};
	std::vector<TCHAR> UserProfileBuffer;

	if SUCCEEDED(SHGetFolderPath(NULL, CSIDL_PROFILE, NULL, 0, UserProfilePath))
	{
		for (unsigned i = 0; (UserProfilePath[i] != '\0'); i++)
		{
			if (i > 8)
				UserProfileBuffer.push_back(UserProfilePath[i]);
		}

		m_UserName = std::wstring(UserProfileBuffer.begin(), UserProfileBuffer.end());
		m_UserProfile = std::wstring(UserProfilePath);
	}
}
