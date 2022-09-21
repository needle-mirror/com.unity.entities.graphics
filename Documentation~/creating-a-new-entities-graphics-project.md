# Creating a new Entities Graphics project

1. Create a new project. Depending on which render pipeline you want to use with Entities Graphics, the project should use a specific template:
	* For the Universal Render Pipeline (URP), use the **Universal Render Pipeline** template.
	* For the High Definition Render Pipeline (HDRP), use the **High Definition RP** template.
	* Entities Graphics doesn't support the Built-in Render Pipeline (**3D** template). 
2. Install the Entities Graphics package. Since this is an experimental package, it's not visible in the Package Manager window. The most consistent way to install this package for all versions of Unity is to use the [manifest.json](https://docs.unity3d.com/Manual/upm-manifestPrj.html).
	1. In the Project window, go to **Packages** and right-click in an empty space.
	2. Click **Show in Explorer** then, in the File Explorer window, open **Packages > manifest.json**.
	3. Add `"com.unity.entities.graphics": "*<package version>*"` to the list of dependencies where \<version number> is the version of the Entities Graphics Package you want to install. For example:<br/>`"com.unity.entities.graphics": "0.x.y"`
	4. Installing the Entities Graphics package also installs all of its dependencies including the DOTS packages.
3. Make sure SRP Batcher is enabled in your Project's URP or HDRP Assets. Creating a Project from the URP or HDRP template enables SRP Batcher automatically.
	* **URP**: Select the [URP Asset](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest?subfolder=/manual/universalrp-asset.html) and view it in the Inspector, go to **Advanced** and make sure **SRP Batcher** is enabled.
	* **HDRP**: Select the [HDRP Asset](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@latest?subfolder=/manual/index.html) and view it in the Inspector, enter [Debug Mode ](https://docs.unity3d.com/Manual/InspectorOptions.html)for the Inspector, and make sure **SRP Batcher** is enabled.
4. Entities Graphics does not support gamma space. Your Project must use linear color space. To do this:
   1. Go to **Edit > Project Settings > Player > Other Settings** and locate the **Color Space** property.
   2. Select **Linear** from the **Color Space** drop-down.
5. Entities Graphics is now installed and ready to use.
