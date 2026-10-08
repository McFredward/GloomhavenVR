#!/usr/bin/env python3
"""Transfer complete local native Gloomhaven saves, without cloud/provider services.

Quest commands reuse the installer's verified Wi-Fi/USB and local ADB bootstrap.
They stop the game before copying. Imports need explicit --replace for an
existing root and retain complete remote and PC backups before committing.
"""
from pathlib import Path
import argparse
from datetime import datetime,timezone
import json
import shlex
import sys
import tempfile
import uuid

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/"tools/quest-builder"))
import save_export
from storage import BuildError,digest
sys.path.insert(0,str(ROOT/"tools/quest-installer"))
import installer
import collector

REMOTE="/sdcard/Android/data/"+installer.PACKAGE+"/files"


def remote_directory_exists(adb,serial,path):
    raw=adb.run("-s",serial,"shell","if test -d "+shlex.quote(path)+"; then printf PRESENT; else printf ABSENT; fi",check_text=False)
    if raw not in ("PRESENT","ABSENT"):
        raise installer.InstallError("Could not determine the selected Quest's native save directory.")
    return raw=="PRESENT"


def transfer_quest(args,runner=None):
    config=installer.read_json(args.config) if args.config.is_file() else {}
    executable=installer.adb_path(args.adb,config.get("adb"))
    output=args.output or args.archive.parent/"quest-save-backups"
    output.parent.mkdir(parents=True,exist_ok=True)
    log_directory=output if args.command=="import-quest" else output.parent
    log_directory.mkdir(parents=True,exist_ok=True)
    adb_log=log_directory/("quest-save-transfer-"+datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")+"-"+uuid.uuid4().hex[:8]+".log")
    with tempfile.TemporaryDirectory(prefix="quest-save-transfer-") as temporary:
        work=Path(temporary);adb=installer.Adb(executable,adb_log,runner)
        serial,hardware,transport=collector.choose_capture_device(adb,args,config)
        manifest=save_export.validate_snapshot(args.archive) if args.command=="import-quest" else None
        root_name=manifest["saveRoot"] if manifest else "GloomSaves"
        remote_root=REMOTE+"/"+root_name
        present=remote_directory_exists(adb,serial,remote_root)
        if args.command=="import-quest" and present and not args.replace:
            raise BuildError("Existing Quest saves retained; use explicit --replace for a complete snapshot import with backup.")
        if args.command=="export-quest" and not present:
            raise BuildError("The installed Quest app has no native GloomSaves root to export.")
        # Original save writes/queues belong to the game. No transfer runs while
        # the process can change its global index or scenario/checkpoint bytes.
        adb.run("-s",serial,"shell","am","force-stop",installer.PACKAGE)
        old=work/"existing";old.mkdir()
        if present:
            adb.run("-s",serial,"pull",remote_root,old,timeout=300,check_text=False)
            if not (old/root_name).is_dir():
                raise BuildError("ADB did not return the complete expected native save root; existing Quest bytes retained.")
        if args.command=="export-quest":
            result=save_export.export_snapshot(old/root_name,args.output)
            result.update(deviceSerial=hardware,transport=transport,gameStopped=True,adbLog=str(adb_log))
            installer.write_json(args.output.with_suffix(args.output.suffix+".receipt.json"),result)
            return result
        local_root=work/root_name
        save_export.import_snapshot(args.archive,local_root)
        marker=json.loads((local_root/save_export.MARKER).read_text())
        id=marker["transferId"];stage_name=".quest-save-import-"+uuid.uuid4().hex
        stage=work/stage_name;local_root.rename(stage)
        backup_relative=save_export.BACKUP_FOLDER+"/"+id+"/"+root_name
        remote_stage=REMOTE+"/"+stage_name;remote_backup=REMOTE+"/"+backup_relative
        pc_backup=None
        if present:
            output.mkdir(parents=True,exist_ok=True);pc_backup=output/("quest-saves-before-import-"+id+".zip")
            save_export.export_snapshot(old/root_name,pc_backup)
        journal={"schema":1,"format":save_export.FORMAT,"transferId":id,"saveRoot":root_name,
                 "staging":stage_name,"backup":backup_relative}
        journal_local=work/(save_export.JOURNAL+".upload-"+uuid.uuid4().hex)
        installer.write_json(journal_local,journal)
        if remote_directory_exists(adb,serial,REMOTE+"/"+save_export.BACKUP_FOLDER+"/"+id):
            raise BuildError("Remote save backup destination unexpectedly exists; Quest saves retained.")
        # Refuse an unresolved earlier transaction instead of overwriting its
        # recovery state. A normal app start resolves it before another import.
        journal_status=adb.run("-s",serial,"shell","if test -e "+shlex.quote(REMOTE+"/"+save_export.JOURNAL)+"; then printf PRESENT; else printf ABSENT; fi",check_text=False)
        if journal_status!="ABSENT":raise BuildError("Quest has a pending save transaction; start the app once to recover it before importing.")
        adb.run("-s",serial,"shell","mkdir -p "+shlex.quote(REMOTE))
        adb.run("-s",serial,"push",stage,REMOTE+"/",timeout=300,check_text=False)
        verify=work/"verify";verify.mkdir()
        adb.run("-s",serial,"pull",remote_stage,verify,timeout=300,check_text=False)
        verified_root=verify/stage_name
        uploaded_marker=verified_root/save_export.MARKER
        if not uploaded_marker.is_file() or uploaded_marker.is_symlink() or digest(uploaded_marker)!=digest(stage/save_export.MARKER):
            raise BuildError("Quest staged recovery marker failed the upload/readback check; native save root was retained.")
        expected_files={row["path"] for row in manifest["files"]}|{save_export.MARKER}
        actual_files=set()
        for path in verified_root.rglob("*"):
            if path.is_symlink():raise BuildError("Quest staged save tree contains a link; native save root was retained.")
            if path.is_file():actual_files.add(path.relative_to(verified_root).as_posix())
        if actual_files!=expected_files:
            raise BuildError("Quest staged save tree has unexpected/missing records; native save root was retained.")
        for row in manifest["files"]:
            path=verify/stage_name/row["path"]
            if not path.is_file() or path.stat().st_size!=row["bytes"] or digest(path)!=row["sha256"]:
                raise BuildError("Quest staged save bytes failed the upload/readback hash check; native save root was retained.")
        adb.run("-s",serial,"push",journal_local,REMOTE+"/",timeout=30,check_text=False)
        adb.run("-s",serial,"shell","mv "+shlex.quote(REMOTE+"/"+journal_local.name)+" "+shlex.quote(REMOTE+"/"+save_export.JOURNAL))
        if present:
            adb.run("-s",serial,"shell","mkdir -p "+shlex.quote(remote_backup.rsplit("/",1)[0]))
            adb.run("-s",serial,"shell","mv "+shlex.quote(remote_root)+" "+shlex.quote(remote_backup))
        # If Wi-Fi disappears here, startup's persisted journal restores the old
        # root or validates/commits the first import. Never remove the backup.
        adb.run("-s",serial,"shell","mv "+shlex.quote(remote_stage)+" "+shlex.quote(remote_root))
        adb.run("-s",serial,"shell","rm "+shlex.quote(REMOTE+"/"+save_export.JOURNAL))
        result={"schema":1,"deviceSerial":hardware,"transport":transport,"saveRoot":remote_root,
                "files":len(manifest["files"]),"archiveSha256":digest(args.archive),
                "remoteBackup":remote_backup if present else None,"pcBackup":str(pc_backup) if pc_backup else None,
                "nativeBytesUnchanged":True,"globalIndexReplaced":True,"gameStopped":True,"cloudServicesUsed":False,"adbLog":str(adb_log)}
        output.mkdir(parents=True,exist_ok=True)
        installer.write_json(output/("quest-save-import-"+id+".receipt.json"),result)
        return result


def parser():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("command",choices=("export-pc","import-pc","export-quest","import-quest","validate"))
    p.add_argument("--save-root",type=Path);p.add_argument("--archive",type=Path);p.add_argument("--output",type=Path)
    p.add_argument("--replace",action="store_true",help="Replace a complete stopped-game save snapshot, retaining the previous root.")
    p.add_argument("--config",type=Path,default=ROOT/".planning/debug/quest3/wireless-install.json")
    p.add_argument("--adb",type=Path);p.add_argument("--host");p.add_argument("--serial");p.add_argument("--setup",action="store_true")
    return p


def main(argv=None,runner=None):
    p=parser();args=p.parse_args(argv)
    required={"export-pc":("save_root","output"),"import-pc":("save_root","archive"),
              "export-quest":("output",),"import-quest":("archive",),"validate":("archive",)}[args.command]
    for field in required:
        if getattr(args,field) is None:p.error("--"+field.replace("_","-")+" is required for "+args.command)
    try:
        if args.command=="export-pc":result=save_export.export_snapshot(args.save_root,args.output)
        elif args.command=="import-pc":result=save_export.import_snapshot(args.archive,args.save_root,args.replace)
        elif args.command=="validate":result=save_export.validate_snapshot(args.archive)
        else:result=transfer_quest(args,runner)
        print(json.dumps(result,indent=2,ensure_ascii=False));return 0
    except (BuildError,installer.InstallError,OSError,ValueError) as error:
        print("Quest save transfer stopped: "+str(error),file=sys.stderr);return 1


if __name__=="__main__":raise SystemExit(main())
