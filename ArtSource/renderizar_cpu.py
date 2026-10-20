import sys
from pathlib import Path
import bpy
source_path = Path(sys.argv[sys.argv.index('--') + 1])
if not source_path.is_absolute(): source_path = Path(__file__).resolve().parent / source_path
source = source_path.read_text()
def render_cpu():
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 16
    scene.cycles.use_denoising = True
    bpy.ops.render.render(write_still=True)
source = source.replace('bpy.ops.render.render(write_still=True)', 'render_cpu()')
exec(compile(source, str(source_path), 'exec'), {'__name__': '__main__', '__file__': str(source_path), 'render_cpu': render_cpu})
