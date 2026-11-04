import bpy,os
root=os.path.join(os.path.dirname(os.path.abspath(__file__)),'DeskDismount')
bpy.ops.wm.open_mainfile(filepath=os.path.join(root,'Knight_DeskDismount.blend'))
s=bpy.context.scene
if hasattr(s.render,'media_type'):s.render.media_type='VIDEO'
if hasattr(s.render.image_settings,'media_type'):s.render.image_settings.media_type='VIDEO'
s.render.engine='BLENDER_WORKBENCH'
s.display.shading.light='STUDIO'
s.display.shading.color_type='MATERIAL'
s.display.shading.show_shadows=True
s.display.shading.show_cavity=True
s.render.resolution_x=600;s.render.resolution_y=600
s.render.image_settings.file_format='FFMPEG'
s.render.ffmpeg.format='MPEG4'
s.render.ffmpeg.codec='H264'
s.render.ffmpeg.constant_rate_factor='MEDIUM'
s.render.filepath=os.path.join(root,'Knight_DeskDismount_preview.mp4')
bpy.ops.render.render(animation=True)
