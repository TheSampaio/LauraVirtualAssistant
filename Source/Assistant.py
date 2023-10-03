import time
from Core import System, Voice

class Assistant():
    
    def __init__(self) -> None:
        self.__NAME = "Laura"
        self.__voice = Voice()
        self.__system = System()
        self.__lastHour = 0

    def __Initialize(self):
        self.__voice.Initialize()
        self.__system.Initialize()
        self.__system.Sleep(1.0)

        self.__voice.Speak(f"Good {self.__GetDayPeriod(self.__system.GetTime()[0])} {self.__system.GetUsername()}")
        self.__voice.Speak(f"Today is {self.__system.GetDate()[0]} of {self.__GetMonth(self.__system.GetDate()[1])}, {self.__system.GetDate()[2]}")
        self.__voice.Speak(f"It is now {self.__system.GetTime()[0]} hours and {self.__system.GetTime()[1]} minutes, according to your OS")
        self.__lastHour = self.__system.GetTime()[0]

    def __VerifyHours(self):
        
        if (self.__system.GetTime()[0] != self.__lastHour):
            self.__lastHour = self.__system.GetTime()[0]
            self.__voice.Speak(f"It is now {self.__lastHour} hours")

    def Run(self):
        self.__Initialize()

        while (True):
            self.__VerifyHours()
            time.sleep(1.0)

    # === GET methods ===

    def __GetMonth(self, month : int) -> str:

        match (month):
            case 1: return "January"
            case 2: return "February"
            case 3: return "March"
            case 4: return "April"
            case 5: return "May"
            case 6: return "June"
            case 7: return "July"
            case 8: return "August"
            case 9: return "September"
            case 10: return "October"
            case 11: return "November"
            case 12: return "December"
            case _: return "Invalid month"

    def __GetDayPeriod(self, hour : int) -> str:

        if (hour >= 18):
            return "evening"
        
        elif (hour >= 12):
            return "afternoon"
        
        else:
            return "morning"
