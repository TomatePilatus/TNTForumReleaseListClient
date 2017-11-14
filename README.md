# TNTForumReleaseListClient
Since a couple of years, the website [TNT village](http://www.tntvillage.scambioetico.org/) has been almost inaccessible to the most part of its users. Luckily the webmaster offers a page open to everyone only to search for releases (i.&nbsp;e. http://www.tntvillage.scambioetico.org/?releaselist); nevertheless, the whole page takes minutes to load, just to download an HTML table element which contains magnet links. I have "reverse engineered" the web page and written a CLI client in c# which requests the back-end PHP service.

Once you have compiled the project, just run the following command:

    TNTForumReleaseListClient.exe what you are looking for

and the program will write all records found in a CSV file `output.csv` in the same directory.
