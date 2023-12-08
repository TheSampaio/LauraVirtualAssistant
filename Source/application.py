from time import sleep
from assistant import Assistant

class Application:

    def Run(self, assistant : Assistant):

        assistant.OnStart()

        while(not assistant.Close()):
            assistant.OnUpdate()
            assistant.OnRender()
            sleep(0.05) # 20 FPS

        assistant.OnEnd()
