# Luafuck
Rewrite Lua scripts with 12 characters. Charset: `[]()#._Gchar`  

## Quick-Start
1. Clone
2. Open `Luafuck.sln` in Visual Studio 2022 (Preview)
3. Compile (`ctrl+b`)
4. Run `Luafuck.exe <lua_script_path>` from either 'Debug' or 'Release' dir (based on compilation profile)

## Basics
```
1. ""               (Empty string)    ==> '[[]]'
2. 0                (number)          ==> '#_G' or '#[[]]'
3. "0"              (string)          ==> '[[]] .. #[[]]'
4. "00"             (string)          ==> '#[[]] .. #[[]]'
5. 1                (number)          ==> '#[[.]]'
6. 2                (number)          ==> '#[[..]]'
7. 3                (number)          ==> '#[[...]]'
8. string.char      (func)            ==> '[[]][([[char]])]'
9. 'a'              (char)            ==> Assume 'strchar' holds 'string.char', 'strchar(97)'
10. 'b'             (char)            ==> Assume 'strchar' holds 'string.char', 'strchar(98)'
11. 'abc'           (string)          ==> Assume 'chr_a','chr_b','chr_c' holds the chars, 'char_a .. char_b .. char_c'
12. Globals table                     ==> '_G'
13. A global 'glb'                    ==> '_G[([[glb]])]'
14. loadstring/load (func)            ==> '_G[([[loadstring]])]' or '_G[([[load]])]'
15. Execute code                      ==> Construct the code as a string in memory (like any other 
                                          string, shown above) then call 'loadstring(code_var)()' or 'load(code_var)()'
```

\* Anywhere a string is showing in the examples assume it was constructed by getting individuals chars then concatinating the charaters together.

## Resulting script
When transforming a Lua script to Luafuck using this project you'll always get a new script with the following structure:  
(Assuming Lua version <= 5.1. Otherwise replace with 'load' calls)
```
loadstring(helper_1)()
loadstring(helper_2)()
loadstring(transformed_original_code)()
```
Where `helper_1` and `helper_2` are short functions used to get **short** handles (in variables with short names) to  
`string.char` and a propietry decoder function `r` to be used in the transformed_original_code construction expression.  
Note that `helper_1`, `helper_2` and `transformed_original_code` are strings representing valid lua code.  
The "loadstring" calls obviously do not use the string "loadstring" in the code, it's retrieved from `_G` using the available characters.  

This means this project currently is more of an 'encoder' for Lua scripts, resembling "shellcode encoders" in it's behaviour.
The encoder itself is, of course, written according to Luafuck's constraint but it does not manipulate original code in a more intimate way.

An example of input & output can be found in this repo's Example directory.

---
### Further ideas (feasibility unknown)
* `table.concat` is able to concat strings without the char `.` if we manage to:
    1. Construct a table `t` (note we currently don't use `{}`) where the *array part* contains, in order, the substrings we want to concat. AFAIK this can be done on `_G` with `table.insert`,`table.remove` without obvious side effects.
    2. Somehow retrieve the pointer to `concat` from either the table `t`. Again, `_G` is a good candidate but calling `_G['concat']` forces us to create the string `"concat"` in memory (currently done via concatination) or add `ont` to the charset and use a literal (which means +2 in the charset's size)
* `string.len` is able to get a string's length without the char `#` if we manage to:
    1. Access `len` from `[[]]` (any string will do) which is currently only possible via creating the string `"len"` in memory by using `#` on different sized string to get the ASCII values for the char literals.
