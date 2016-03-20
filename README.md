# cb-logviewer

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
