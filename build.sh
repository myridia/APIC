dotnet publish apic/apic.csproj -c Release -r linux-x64 /p:PublishSingleFile=true /p:PublishTrimmed=true

#scp  log/bin/Release/net10.0/linux-x64//publish/log  root@192.168.80.241:/opt/
#scp  stock/bin/Release/net10.0/linux-x64//publish/stock  root@192.168.80.241:/opt/
