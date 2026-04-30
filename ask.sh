echo -e "What you like to do?, enter a Task Id from list below: \n"
echo -e "TaskID\tFile\t\tDescription"
echo -e "1\t Build "
echo -e "2\t Run "
echo -e "3\t Restores dependencies and tools of a project"
echo -e "4\t List All Packages/Libraries with versions "
echo -e "5\t "
echo -e "6\t "
echo -e "7\t "
echo -e "8\t "
echo -e "9\t "

until [ "$task" = "0" ]; do
  read task

  if [ "$task" = "1" ]; then
    echo "...${task}"
    dotnet publish apic/apic.csproj -c Release -r linux-x64 /p:PublishSingleFile=true /p:PublishTrimmed=true

  elif [ "$task" = "2" ]; then
    echo "...${task}"
    dotnet publish apic/apic.csproj -c Release -r linux-x64 /p:PublishSingleFile=true /p:PublishTrimmed=true
    apic/bin/Release/net10.0/linux-x64/apic

  elif [ "$task" = "3" ]; then
    echo "...${task}"
    dotnet restore
    #scp  log/bin/Release/net10.0/linux-x64//publish/log  root@192.168.80.241:/opt/
    #scp  stock/bin/Release/net10.0/linux-x64//publish/stock  root@192.168.80.241:/opt/
  elif [ "$task" = "4" ]; then
    echo "...${task}"
    dotnet list package

  elif [ "$task" = "5" ]; then
    echo "...${task}"

  elif [ "$task" = "6" ]; then
    echo "...${task}"

  elif [ "$task" = "7" ]; then
    echo "...${task}"

  elif [ "$task" = "8" ]; then
    echo "...${task}"

  elif [ "$task" = "9" ]; then
    echo "...${task}"

  else
    echo "Goodbye! - Exit"
  fi

  sleep 3

  ./ask.sh

done
