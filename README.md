# APIC
APIC API: Application Programming Interface Connector - API (for QuickBooks Desktop)


## HOW to sign the apic with powershell 

### Make the Root Auth
```
makecert -r -pe -n "CN=My CA" -ss CA -a sha256 -cy authority -sky signature -sv MyCA.pvk MyCA.cer
certutil -user -addstore Root MyCA.cer
```



## Make a cert
```
makecert -pe -n "CN=My SPC" -a sha256 -cy end -sky signature -ic MyCA.cer -iv MyCA.pvk -sv MySPC.pvk MySPC.cer
pvk2pfx -pvk MySPC.pvk -spc MySPC.cer -pfx MySPC.pfx
```

## Sing it 
```
signtool sign /v /f MySPC.pfx /t http://timestamp.digicert.com /fd sha256 apic_server.exe
signtool verify /pa "apic_server.exe"
```
