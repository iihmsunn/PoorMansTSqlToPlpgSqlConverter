#!/usr/bin/env bash

executable=./bin/Release/net9.0/tsql2psql

while getopts ":i:o:" opt; do
  case $opt in
    i) inputdir="$OPTARG"
    ;;
    o) outputdir="$OPTARG"
    ;;
    \?) echo "Invalid option -$OPTARG" >&2
    exit 1
    ;;
  esac

  case $OPTARG in
    -*) echo "Option $opt needs a valid argument"
    exit 1
    ;;
  esac
done

for filename in $inputdir/*.sql; do
  echo "Processing $filename..."
  $executable -i $filename -o $outputdir/$(basename $filename)
done
