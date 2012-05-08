MultiProxyServer
=============

A proxy that not only let you run multiple virtual Minecraft servers but can also handle diffrent protocols all of which on one port.
Current supported protocols:
 * Minecraft
 * IRC (unencrypted)
 * HTTP
 * HTTPS*
 
*note: without client authentication works, I haven't tested the other ones.
You will need to add the correct certificates.

Useages and configuration
-------
there are ony 2 commands:
 * reload - this will reload the configuration file.
 * quit - exit the program.
 
When you run the program for the first time a config wil be generated in the same directory.
You can also start with the config as a parameter. Example:

	MultiProxy.exe C:\someDir\aConfigFile.xml



Special Thanks
-------
 * [the Minecraft Coalition wiki] (http://mc.kev009.com/Protocol)
 * [Drew DeVault] (https://github.com/SirCmpwn) because MultiProxyServer is based on my improved version of [MCVHost] (https://github.com/SirCmpwn/MCVHost)