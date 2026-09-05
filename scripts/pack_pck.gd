# Packs godot/<rel> files into res://pvpduel/<rel> inside the mod PCK.
# Usage: godot --headless --path <repo> --script scripts/pack_pck.gd -- <out.pck> [<manifest.json>]
extends SceneTree

const ModId := "pvpduel"

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 1:
		printerr("Usage: pack_pck.gd <out.pck> [mod_manifest.json]")
		quit(2)
		return

	var out_pck: String = args[0]
	var repo_root: String = ProjectSettings.globalize_path("res://")
	var source_dir := repo_root.path_join("godot")

	var packer := PCKPacker.new()
	var err := packer.pck_start(out_pck)
	if err != OK:
		printerr("pck_start failed: ", err)
		quit(10)
		return

	var file_count := 0
	for path in _list_files(source_dir):
		var rel := path.substr(source_dir.length() + 1).replace("\\", "/")
		var res_path := "res://%s/%s" % [ModId, rel]
		err = packer.add_file(res_path, path)
		if err != OK:
			printerr("add_file failed for ", res_path, ": ", err)
			quit(11)
			return
		file_count += 1
		print("  + ", res_path)

	err = packer.flush()
	if err != OK:
		printerr("flush failed: ", err)
		quit(12)
		return

	print("Packed %d file(s) into %s" % [file_count, out_pck])
	quit(0)

func _list_files(dir: String) -> PackedStringArray:
	var files := PackedStringArray()
	var d := DirAccess.open(dir)
	if d == null:
		printerr("Cannot open directory: ", dir)
		quit(3)
		return files
	d.list_dir_begin()
	var name := d.get_next()
	while name != "":
		var full := dir.path_join(name)
		if d.current_is_dir():
			files.append_array(_list_files(full))
		else:
			files.append(full)
		name = d.get_next()
	d.list_dir_end()
	return files
