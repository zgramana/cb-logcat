# cb-logcat

To build, after cloning run `git submodule update --init --recursive`. Then, run these two commands:
```bash
export MONO_PATH=/Library/Frameworks/Mono.framework/Versions/Current/bin
export PATH=$PATH:$MONO_PATH
```
After that:
 1. go into `vendor/mono-curses`
 2. run `configure`
 3. run `make`
 4. run `make install`.
 5. Finally, from the same path, run `xbuild`.

From the root of the repo, you should now be able to `xbuild` successfully.

## Usage

```
usage: cb-logcat [options] [directory path]

  -s, --skip-import          skip importing the logs
  -f, --file=VALUE           database file path
  -h, --help                 show this message and exit
```

1. Build the app from source or use the latest release from [here](https://github.com/zgramana/cb-logcat/releases).
2. Import and merge logcat files (if the file path you pass to -f doesn't exist, then `cb-logcat` will create a new one at that location):

	```
	mono cb-logcat.exe -f /path/to/android.cblite2 /path/to/03-19-2016_13-16-33-android-dump
	```

3. Run queries:

	```
	mono cb-logcat.exe -s -f /path/to/android.cblite2 /path/to/03-19-2016_13-16-33-android-dump
	```

	The program will prompt you to enter start and end times in ISO format like so:

	```
	Enter your start time: 2016-03-19T05:57:51.462-07:00
	Enter your stop time: 2016-03-19T15:57:51.462-07:00
	```
