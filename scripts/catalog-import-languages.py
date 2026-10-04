"""Finite, resumable operator bootstrap. The API process owns card/asset/embedding ingestion."""
import argparse
import datetime
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.request

LANGUAGES=("en","pt","pt-br","es","es-mx","ja","fr","de","it","ko","zh-tw","zh-cn","id","th","nl","pl","ru","pt-pt")

def validate_languages(values):
    if not values or any(x not in LANGUAGES for x in values) or len(set(values))!=len(values):
        raise ValueError("Unsupported or duplicate provider locale.")
    return list(values)

def parse_report(output):
    decoder=json.JSONDecoder();result={}
    for match in re.finditer(r"(?m)^\{",output):
        try:
            value,_=decoder.raw_decode(output[match.start():])
            if isinstance(value,dict):
                if isinstance(value.get("sync"),dict):result=value
                elif "id" in value and value.get("status") in ("completed","partial","failed"):result={"sync":value}
        except (ValueError,TypeError):pass
    return result

def classify(exit_code,report):
    if exit_code not in (0,1):return "failed"
    if report.get("sync",{}).get("status") not in ("completed","partial"):return "failed"
    return "completed" if exit_code==0 and report["sync"]["status"]=="completed" and ("preparation" not in report or report["preparation"].get("complete") is True) else "partial"

def pipeline_status(state,languages):
    metadata=all(state.get("languages",{}).get(lang,{}).get("status") in ("completed","no_data") for lang in languages)
    ready=metadata and all(state.get(phase,{}).get("status")=="completed" for phase in ("artwork","vision"))
    return "completed" if ready else "completed_with_pending"

def artwork_arguments():
    return ["-e","Vision__ModelManifestPath=","-e","Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning",
        "-e","Logging__LogLevel__System.Net.Http.HttpClient=Warning","vaulta-api","--catalog-assets-import","all"]

def ensure_container(name,exists,launch):
    if exists(name):return "existing"
    launch(name);return "started"

def now():return datetime.datetime.now(datetime.timezone.utc).isoformat()
def run(args,check=True):return subprocess.run(args,check=check,capture_output=True,text=True)
def exists(name):return run(["docker","inspect",name],False).returncode==0

def main():
    import fcntl
    parser=argparse.ArgumentParser()
    parser.add_argument("--root",default="/opt/vaulta")
    parser.add_argument("--languages",default=",".join(LANGUAGES))
    parser.add_argument("--follow-en",help="Adopt a previously started English import instead of duplicating it.")
    parser.add_argument("--phase",choices=["metadata","artwork","vision"],
        help="Run only specific phase: metadata (TCGdex sync), artwork (download/S3), vision (embeddings). Default: all phases.")
    args=parser.parse_args();languages=validate_languages(args.languages.split(","))
    root=Path(args.root).resolve();state_dir=root/".deploy/catalog-bootstrap";state_dir.mkdir(parents=True,exist_ok=True)
    lock=open(state_dir/"run.lock","w");fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
    state_file=state_dir/"status.json"
    state=json.loads(state_file.read_text()) if state_file.exists() else {"startedAt":now(),"languages":{}}
    tag=(root/".deploy/current-tag").read_text().strip()
    if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}",tag) or tag=="latest":raise ValueError("Use a validated immutable deployment tag.")
    envfile=root/".env.production"
    if envfile.stat().st_mode & 0o077:raise ValueError("Production secrets file must remain private.")
    os.environ["IMAGE_TAG"]=tag
    compose=["docker","compose","--project-name","vaulta-production","--env-file",str(envfile),"-f",str(root/"docker-compose.production.yml")]
    run(compose+["config","--quiet"])
    def save():
        state["updatedAt"]=now()
        temporary=state_file.with_suffix(".tmp");temporary.write_text(json.dumps(state,indent=2)+"\n");temporary.chmod(0o600);temporary.replace(state_file)
    def finish(lang,name):
        entry=state["languages"][lang];entry["status"]="running";entry["container"]=name;save()
        code=int(run(["docker","wait",name]).stdout.strip())
        report=parse_report(run(["docker","logs","--tail","2000",name]).stdout)
        entry.update({"status":classify(code,report),"exitCode":code,"finishedAt":now()})
        sync=report.get("sync",{});preparation=report.get("preparation",{})
        entry["report"]={k:sync[k] for k in ("id","status","scope","recordsRead","recordsCreated","recordsUpdated","recordsUnresolved") if k in sync}
        entry["artwork"]=preparation.get("artwork",{})
        refs=preparation.get("references",{});entry["references"]={k:refs[k] for k in ("generated","unchanged","pending","failed","complete") if k in refs}
        entry["index"]={k:refs.get("index",{}).get(k) for k in ("version","referenceCount")}
        save()
    phase=args.phase
    # Artwork and vision phases run once globally, not per-language
    if phase=="artwork":
        entry=state.get("artwork",{})
        if entry.get("status") in ("completed","partial"):
            print(json.dumps({"status":"skipped","phase":"artwork","reason":"already completed"}))
            return
        name="vaulta-artwork-all-"+tag[:24]
        state["artwork"]={"status":"pending","startedAt":now(),"container":name};save()
        def launch_artwork(job):
            run(compose+["run","-d","--no-deps","--name",job]+artwork_arguments())
            run(["docker","update","--cpus",".5","--memory","768m","--memory-swap","1280m",job])
        ensure_container(name,exists,launch_artwork)
        entry=state["artwork"];entry["status"]="running";save()
        code=int(run(["docker","wait",name]).stdout.strip())
        entry.update({"status":"completed" if code==0 else "failed","exitCode":code,"finishedAt":now()});save()
        print(json.dumps({"status":entry["status"],"phase":"artwork","exitCode":code}))
        return
    if phase=="vision":
        entry=state.get("vision",{})
        if entry.get("status") in ("completed","partial"):
            print(json.dumps({"status":"skipped","phase":"vision","reason":"already completed"}))
            return
        manifest=os.environ.get("VISION_MODEL_MANIFEST_PATH","/models/clip-base/manifest.json")
        name="vaulta-vision-build-"+tag[:24]
        state["vision"]={"status":"pending","startedAt":now(),"container":name};save()
        def launch_vision(job):
            run(compose+["run","-d","--no-deps","--name",job,
                "-e","Vision__ModelManifestPath="+manifest,
                "-e","Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning",
                "vaulta-api","--vision-index-build",manifest])
            run(["docker","update","--cpus",".5","--memory","768m","--memory-swap","1280m",job])
        ensure_container(name,exists,launch_vision)
        entry=state["vision"];entry["status"]="running";save()
        code=int(run(["docker","wait",name]).stdout.strip())
        entry.update({"status":"completed" if code==0 else "failed","exitCode":code,"finishedAt":now()});save()
        print(json.dumps({"status":entry["status"],"phase":"vision","exitCode":code}))
        return
    # Metadata phase (default when --phase is omitted or set to metadata)
    for lang in languages:
        entry=state["languages"].get(lang,{})
        if entry.get("status") in ("completed","partial","failed","no_data"):continue
        if entry.get("container") and exists(entry["container"]):finish(lang,entry["container"]);continue
        if lang=="en" and args.follow_en and exists(args.follow_en):
            state["languages"][lang]={"imageTag":"adopted","startedAt":now()};finish(lang,args.follow_en);continue
        if phase=="metadata" or phase is None:
            try:
                with urllib.request.urlopen("https://api.tcgdex.net/v2/"+lang+"/sets",timeout=60) as response:sets=json.load(response)
                if not isinstance(sets,list):raise ValueError("Invalid provider set list.")
            except Exception as error:
                state["languages"][lang]={"status":"probe_failed","error":type(error).__name__};save();continue
            if not sets:state["languages"][lang]={"status":"no_data","sets":0};save();continue
            name="vaulta-catalog-"+lang+"-all-"+tag[:24]
            state["languages"][lang]={"status":"pending","sets":len(sets),"imageTag":tag,"startedAt":now(),"container":name};save()
            def launch(job):
                run(compose+["run","-d","--no-deps","--name",job,
                    "-e","Catalog__Providers__TcgDex__Language="+lang,"-e","Catalog__Providers__TcgDex__MaxConcurrency=2",
                    "-e","Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning","-e","Logging__LogLevel__System.Net.Http.HttpClient=Warning",
                    "vaulta-api","--catalog-sync","tcgdex","all"])
                run(["docker","update","--cpus",".5","--memory","768m","--memory-swap","1280m",job])
            ensure_container(name,exists,launch);finish(lang,name)
    # When no phase specified, run artwork and vision after metadata completes
    if phase is None:
        artwork_entry=state.get("artwork",{})
        if artwork_entry.get("status") not in ("completed","partial"):
            name="vaulta-artwork-all-"+tag[:24]
            state["artwork"]={"status":"pending","startedAt":now(),"container":name};save()
            def launch_artwork(job):
                run(compose+["run","-d","--no-deps","--name",job]+artwork_arguments())
                run(["docker","update","--cpus",".5","--memory","768m","--memory-swap","1280m",job])
            ensure_container(name,exists,launch_artwork)
            artwork_entry=state["artwork"];artwork_entry["status"]="running";save()
            code=int(run(["docker","wait",name]).stdout.strip())
            artwork_entry.update({"status":"completed" if code==0 else "failed","exitCode":code,"finishedAt":now()});save()
        vision_entry=state.get("vision",{})
        if vision_entry.get("status") not in ("completed","partial"):
            manifest=os.environ.get("VISION_MODEL_MANIFEST_PATH","/models/clip-base/manifest.json")
            name="vaulta-vision-build-"+tag[:24]
            state["vision"]={"status":"pending","startedAt":now(),"container":name};save()
            def launch_vision(job):
                run(compose+["run","-d","--no-deps","--name",job,
                    "-e","Vision__ModelManifestPath="+manifest,
                    "-e","Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning",
                    "vaulta-api","--vision-index-build",manifest])
                run(["docker","update","--cpus",".5","--memory","768m","--memory-swap","1280m",job])
            ensure_container(name,exists,launch_vision)
            vision_entry=state["vision"];vision_entry["status"]="running";save()
            code=int(run(["docker","wait",name]).stdout.strip())
            vision_entry.update({"status":"completed" if code==0 else "failed","exitCode":code,"finishedAt":now()});save()
    state["status"]=pipeline_status(state,languages) if phase is None else ("metadata_completed" if all(state["languages"].get(lang,{}).get("status") in ("completed","no_data") for lang in languages) else "completed_with_pending")
    save()
    print(json.dumps({"status":state["status"],"languages":{k:v.get("status") for k,v in state["languages"].items()}}))
if __name__=="__main__":main()