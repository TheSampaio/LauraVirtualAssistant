import time
import pyttsx3
import getpass
import datetime

class System():

    def __init__(self) -> None:
        self.__username = getpass.getuser()

    # === MAIN methods ===

    def Sleep(self, amount : float):
        time.sleep(amount)

    # === GET methods ===

    def GetDate(self) -> str:
        return [datetime.datetime.today().day, datetime.datetime.today().month, datetime.datetime.today().year]
    
    def GetTime(self) -> str:
        return [datetime.datetime.today().hour, datetime.datetime.today().minute]

    def GetUsername(self) -> str:
        return self.__username
    
    def GetMonth(self, month : int) -> str:

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

    def GetDayPeriod(self, hour : int) -> str:

        if (hour >= 18):
            return "evening"
        
        elif (hour >= 12):
            return "afternoon"
        
        else:
            return "morning"

class Voice():

    def __init__(self) -> None:
        self.__id = pyttsx3.init()
        self.__voices = self.__id.getProperty("voices")
        self.__id.setProperty("voice", self.__voices[0 if (len(self.__voices) > 0) else 0].id)

    def Speak(self, text : str):

        try:
            print(f"Laura: {text}.")
            self.__id.say(text)
            self.__id.runAndWait()

        except:
            print(f"Laura: {text}.")

