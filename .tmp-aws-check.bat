@echo off
where aws > "%~dp0.tmp-aws-path.txt" 2>&1
aws sts get-caller-identity > "%~dp0.tmp-aws-identity.txt" 2>&1