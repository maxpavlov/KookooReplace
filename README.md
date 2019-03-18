# KookooReplace
Recursivelly replaces files in subdirectories with a given file, including the archives

https://www.maxpavlov.com/2016/04/15/kookoo-replace/

Usages:
For SQL mode:
-m sqlimage -f C:\path\to\etalon\File.class -c "ConnectionStringToDB" -t dbo.TABLENAME -F FILENAME -i DATA -a jar -u IDCOLUMNNAME

For folder mode:
-m folder -f C:\path\to\etalon\File.class -r "C:\path\to\root\where\replace\stars" -a jar

See KookooReplace.exe --help for details on each parameter mentioned.