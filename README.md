# Ark Survival Ascended RCon
 Basic RCon for Ark Survival Ascended Servers

Edit "ARK Survival Ascended Dedicated Server\ShooterGame\Saved\Config\WindowsServer\GameUserSettings.ini"
```
[ServerSettings]
RCONPort=27020
RCONEnabled=true
```

```
cd ASA_RCon\release\R0001
./asa_rcon --Settings:RConHost localhost --Settings:RConPort=27020 --Settings:RConPassword AdminPassword
```

After authenticated, type in a valid command and hit enter to execute it.
Such as
```
ListPlayers
ServerChat Message
SaveWorld
DoExit
```

Just hit enter with no command to send ListPlayers