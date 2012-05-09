MultiProxyServer
=============

A proxy that not only let you run multiple virtual Minecraft servers but can also handle different protocols all of which on one port.
Current supported protocols:
 * Minecraft
 * IRC (unencrypted)
 * HTTP
 * HTTPS*
 
*note: without client authentication works, I haven't tested the other ones.

You will also need to add the correct certificates.

Usage
-------
there are ony 2 commands:
 * reload - this will reload the configuration file.
 * quit - exit the program.
 
When you run the program for the first time a config wil be generated in the same directory.
You can also start with the config as a parameter. Example:

	MultiProxy.exe C:\someDir\aConfigFile.xml

Configuration
-------
	<?xml version="1.0" encoding="utf-8" ?>
	<multiproxyserver>
	  <port>1212</port>
	  <endpoint>0.0.0.0</endpoint>
	  <motd>A Minecraft Proxy</motd>
	  <pingremote>true</pingremote>
	  <usersinmotd>true</usersinmotd>
	  <vhosts>
		<vhost host="example.com" destination="localhost:25565" />
		<vhost host="test.example.com" destination="localhost:25569" />
		<vhost host="alpha.example.com" destination="localhost" />
		<default destination="localhost:25565"></default>
	  </vhosts>
	  <webserver>qwaxys-mini:80</webserver>
	  <ircserver>irc.kreynet.org:6667</ircserver>
	</multiproxyserver>
	
port: The proxyserver wil be listening on this port. Defaults to 25565

motd: The message of the day is the same for each server. Defaults to "A Minecraft Proxy".

pingremote: If enabled the playercount and max player will be the sum of vhosts. Defaults to false.

usersinmotd: If enabled the motd will be appended with a list of all servers and the amount of players, example: "A Minecraft Proxy Servername (3/20) ServernameTwo (1/20)".

vhosts: A list of virtual servers.

vhost: A virtual server, host is the address that a client connects to, destination is where the server actualy runs. The port number is optional.

webserver: The webserver to where all requests are forwarded. You don't need to add virtual hosts here, the webserver should handle that.

Defaults to "localhost", the port defaults to 80.

When a browsers connects using HTTPS then the proxy will allways use 443.

ircserver: The IRC server. Defaults to "localhost", the port defaults to 6667.

Special Thanks
-------
 * [the Minecraft Coalition wiki] (http://mc.kev009.com/Protocol)
 * [Drew DeVault] (https://github.com/SirCmpwn) because MultiProxyServer is based on my improved version of [MCVHost] (https://github.com/SirCmpwn/MCVHost).