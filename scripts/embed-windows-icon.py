"""Embed and verify every ICO size in a Windows executable using native resources."""
import ctypes as C, struct, sys, json
from pathlib import Path
from ctypes import wintypes as W
k=C.WinDLL('kernel32',use_last_error=True)
k.LoadLibraryExW.argtypes=[W.LPCWSTR,W.HANDLE,W.DWORD];k.LoadLibraryExW.restype=W.HMODULE
k.FreeLibrary.argtypes=[W.HMODULE]
k.BeginUpdateResourceW.argtypes=[W.LPCWSTR,W.BOOL];k.BeginUpdateResourceW.restype=W.HANDLE
k.UpdateResourceW.argtypes=[W.HANDLE,C.c_void_p,C.c_void_p,W.WORD,C.c_void_p,W.DWORD];k.UpdateResourceW.restype=W.BOOL
k.EndUpdateResourceW.argtypes=[W.HANDLE,W.BOOL];k.EndUpdateResourceW.restype=W.BOOL
Callback=C.WINFUNCTYPE(W.BOOL,W.HMODULE,C.c_void_p,C.c_void_p,C.c_ssize_t)
k.EnumResourceNamesW.argtypes=[W.HMODULE,C.c_void_p,Callback,C.c_ssize_t];k.EnumResourceNamesW.restype=W.BOOL
LangCallback=C.WINFUNCTYPE(W.BOOL,W.HMODULE,C.c_void_p,C.c_void_p,W.WORD,C.c_ssize_t)
k.EnumResourceLanguagesW.argtypes=[W.HMODULE,C.c_void_p,C.c_void_p,LangCallback,C.c_ssize_t];k.EnumResourceLanguagesW.restype=W.BOOL
k.FindResourceExW.argtypes=[W.HMODULE,C.c_void_p,C.c_void_p,W.WORD];k.FindResourceExW.restype=W.HRSRC
k.LoadResource.argtypes=[W.HMODULE,W.HRSRC];k.LoadResource.restype=W.HGLOBAL
k.LockResource.argtypes=[W.HGLOBAL];k.LockResource.restype=C.c_void_p
k.SizeofResource.argtypes=[W.HMODULE,W.HRSRC];k.SizeofResource.restype=W.DWORD

def fail():raise C.WinError(C.get_last_error())
def resource_name(ptr):return ptr if ptr<=65535 else C.wstring_at(ptr)
def pointer(name):return C.c_void_p(name) if isinstance(name,int) else C.cast(C.c_wchar_p(name),C.c_void_p)
def load(exe):
 h=k.LoadLibraryExW(str(exe.resolve()),None,0x22)
 if not h:fail()
 return h

def names(h,kind):
 found=[]
 @Callback
 def each(module,typ,name,param):found.append(resource_name(name));return True
 if not k.EnumResourceNamesW(h,kind,each,0) and C.get_last_error() not in (1813,1814,1815):fail()
 return found

def langs(h,kind,name):
 found=[]
 @LangCallback
 def each(module,typ,n,lang,param):found.append(lang);return True
 if not k.EnumResourceLanguagesW(h,kind,pointer(name),each,0):fail()
 return found

def read(h,kind,name,lang):
 r=k.FindResourceExW(h,kind,pointer(name),lang)
 if not r:fail()
 data=k.LoadResource(h,r);addr=k.LockResource(data)
 if not addr:fail()
 return C.string_at(addr,k.SizeofResource(h,r))

def main():
 exe,ico=map(Path,sys.argv[1:]);raw=ico.read_bytes();reserved,kind,count=struct.unpack_from('<HHH',raw)
 if reserved!=0 or kind!=1 or not count:raise ValueError('Invalid ICO')
 entries=[]
 for i in range(count):
  width,height,colors,res,planes,bits,size,offset=struct.unpack_from('<BBBBHHII',raw,6+i*16)
  if offset+size>len(raw):raise ValueError('Truncated ICO')
  entries.append((width,height,colors,res,planes,bits,raw[offset:offset+size]))
 h=load(exe)
 try:
  groups=[(name,lang) for name in names(h,14) for lang in langs(h,14,name)] or [(1,1033)]
  ids=[n for n in names(h,3) if isinstance(n,int)];first=max(ids,default=0)+1
 finally:k.FreeLibrary(h)
 if first+count>65535:raise ValueError('No free icon resource IDs')
 group=struct.pack('<HHH',0,1,count)+b''.join(struct.pack('<BBBBHHIH',*entry[:6],len(entry[6]),first+i) for i,entry in enumerate(entries))
 update=k.BeginUpdateResourceW(str(exe.resolve()),False)
 if not update:fail()
 try:
  def put(kind,name,lang,data):
   buf=C.create_string_buffer(data)
   if not k.UpdateResourceW(update,kind,pointer(name),lang,buf,len(data)):fail()
  for lang in set(lang for name,lang in groups):
   for i,entry in enumerate(entries):put(3,first+i,lang,entry[6])
  for name,lang in groups:put(14,name,lang,group)
 except BaseException:k.EndUpdateResourceW(update,True);raise
 if not k.EndUpdateResourceW(update,False):fail()
 h=load(exe)
 try:
  for name,lang in groups:
   assert read(h,14,name,lang)==group
   for i,entry in enumerate(entries):assert read(h,3,first+i,lang)==entry[6]
 finally:k.FreeLibrary(h)
 print(json.dumps({'iconEmbedded':True,'sizes':count,'groupsVerified':len(groups)}))

if __name__=='__main__':main()
