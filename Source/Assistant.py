from core import System, Voice

class Assistant():
    
    def __init__(self) -> None:
        self.__system = System()
        self.__voice = Voice()

        self.__username = self.__system.GetUsername()

        self.__close = False
        self.__lastHour = 0

    # === MAIN methods ===

    def Close(self):
        return self.__close
    
    # === EVENTS ===

    def OnStart(self):
        self.__Greetings()

    def OnUpdate(self):
        self.__VerifyHours()

    def OnEnd(self):
        pass

    # === ACTIONS ===

    def __Greetings(self):
        self.__voice.Speak(f"Good {self.__system.GetDayPeriod(self.__system.GetTime()[0])} {self.__username}")
        self.__voice.Speak(f"Today is {self.__system.GetDate()[0]} of {self.__system.GetMonth(self.__system.GetDate()[1])}, {self.__system.GetDate()[2]}")
        self.__voice.Speak(f"It is now {self.__system.GetTime()[0]} hours and {self.__system.GetTime()[1]} minutes, according to your OS")
        self.__lastHour = self.__system.GetTime()[0]

    def __VerifyHours(self):
        if (self.__system.GetTime()[0] != self.__lastHour):
            self.__lastHour = self.__system.GetTime()[0]

            if (self.__lastHour == '0'):
                self.__voice.Speak(f"It is now midnight")

            else:
                self.__voice.Speak(f"It is now {self.__lastHour} hours")
