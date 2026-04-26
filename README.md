<h1> Project File Manager </h1>

A Unity Tool made for managing your project files, either to delete unused ones or find out if there is a big file lost in your project.

<h2> Unused Finder </h2>

Scan selected folder for assets that are not being used, move all selected assets after scan to a folder named Unused or to the selected folder.<br><br>
&emsp;<b>● Source Folder</b> scan selected Folder.<br>
&emsp;<b>● Target subfolder</b> move all selected unused assets to targt folder, by defult it moves everthing to a folder named <b>Unused</b>.<br>
&emsp;<b>● Extensions</b> all file extensions that the tool will try to look for.<br>

You can either move the assets as a backup or delete them right away. There are some limitations to which files it will select. For example, it will first select a prefab and its materials if they are not being used by any other asset or referenced in a scene. After deleting the unused prefab and materials, the next scan will show all the textures that were being used by the prefab and its materials.<br><br>


<h2> Size Explorer </h2>

It basically scans the <b>Assets</b> folder for all file sizes and displays them in a list where you can sort by size to identify any files that are taking up too much space in the project.<br><br>
