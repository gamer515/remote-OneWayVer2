EmacEArt Stylized Procedural Sky (URP)
======================================

A fully procedural sky for Unity Universal Render Pipeline. Gradient, sun disc
and glow, atmospheric haze, animated clouds and a star field are all generated
in the shader. No panorama texture, no cubemap and no post-processing volume
are needed for the sky itself.


REQUIREMENTS
------------

  Unity            2022.3 LTS or newer
  Render pipeline  Universal Render Pipeline (URP)

The shader includes the URP shader library, so it does not compile in the
Built-in pipeline or in HDRP.


WHAT IS IN THE PACKAGE
----------------------

  Assets/EmacEArt/SkyboxURP/
    Demo/Scripts/    the control panel used by the demo scene
    Documentation/   README, contents list, license and the full PDF manual
    Editor/          custom material inspector (editor only, never in a build)
    Materials/       EA_Skybox plus three plain materials for the demo props
    Scenes/          EA_SkyboxURP_Demo - the demo scene
    Shaders/         EmacSkyboxURP.shader
    LICENSE.txt


START HERE: THE DEMO SCENE
--------------------------

Open Assets/EmacEArt/SkyboxURP/Scenes/EA_SkyboxURP_Demo.unity and press Play.

  - Eight presets across the top: Clear day, Soft clouds, Golden hour, Sunset,
    Overcast, Storm, Toon bands, Night. One click puts the whole sky, the sun
    angle and the cloud settings into that look.
  - Sliders for everything the sky is made of: horizon, haze, sun size and
    halo, side glow, brightness, contrast, colour strength, and nine cloud
    controls.
  - Sun compass and Sun height move the scene light itself, so the shadows on
    the ground follow the sun you see in the sky.
  - Clouds and Stars switch on and off.
  - H or Tab hides the panel for a clean screenshot.

The panel never writes to the material on disk. It clones EA_Skybox into memory
when you press Play and puts the original back when you stop, so you can push
every slider to the end and lose nothing.

Found a look you want to keep? Note the values off the panel and type them into
your own material, or duplicate EA_Skybox and set them there.


USING IT IN YOUR OWN SCENE
--------------------------

  1. Window > Rendering > Lighting > Environment.
  2. Set Skybox Material to EA_Skybox.
  3. For the sky to light your scene, set Environment Lighting > Source to
     Skybox in the same window.
  4. Rotate your Directional Light. The sun disc, its halo and the shadows stay
     in agreement, because Sun Follows Main Light is on.

Duplicate EA_Skybox before you start tuning. The original then stays as a
reference set of values to return to.


THINGS WORTH KNOWING
--------------------

  - The shipped material is a tuned daytime sky: Cotton clouds drifting
    slowly, a warm gradient on the sun side and light haze at the horizon.

  - Stars are off in this material and the cubemap slot is empty. For a night
    sky switch Stars Enabled on and assign a star cubemap - bright texels
    above the threshold become stars.

  - Clouds and stars are shader keywords. Switched off, their code is not in
    the compiled shader at all, so they cost nothing.

  - The sky itself needs no scripts at runtime. The only runtime script in the
    package is the demo panel, and it lives in the Demo folder - delete that
    folder and the sky keeps working.

  - Every control is explained parameter by parameter, with its default value,
    in Documentation/EmacEArt_SkyboxURP_Manual.pdf.


IF THE SKY IS MAGENTA
---------------------

The project is not running URP, or the URP package is missing. Install
Universal RP from the Package Manager and assign a URP asset in
Project Settings > Graphics.


LICENSE
-------

EmacEArt Asset License - use the assets in your own projects, personal and
commercial alike. No credit is required, though it is always welcome.

One favour in return: please do not resell the assets or pass them on as
standalone files or inside another asset pack. This is a one-person studio,
and that goodwill is what pays for the next pack.


CONTACT
-------

EmacEArt - emaceart@gmail.com - https://emaceart.itch.io