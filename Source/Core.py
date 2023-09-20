import os
import time
import pyttsx3
import getpass
import datetime

class System():

    def __init__(self) -> None:
        self.__username = "sir"
        self.__date = [datetime.datetime.today().day, datetime.datetime.today().month, datetime.datetime.today().year]
        self.__time = [datetime.datetime.today().hour, datetime.datetime.today().minute]

    def Initialize(self):
        self.__username = getpass.getuser()

    def Sleep(self, amount : float):
        time.sleep(amount)

    # === GET methods ===

    def GetDate(self) -> str:
        return self.__date
    
    def GetTime(self) -> str:
        return self.__time

    def GetUsername(self) -> str:
        return self.__username

class Voice():

    def __init__(self) -> None:
        self.__id = None
        self.__voices = []

    def Initialize(self):
        self.__id = pyttsx3.init()
        self.__voices = self.__id.getProperty("voices")
        self.__id.setProperty("voice", self.__voices[2 if (len(self.__voices) > 0) else 0].id)

    def Speak(self, text : str):
        self.__id.say(text)
        self.__id.runAndWait()
