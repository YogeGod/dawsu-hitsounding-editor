using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using NAudio.Wave;
using NAudio.CoreAudioApi;

// Binary stdin: byte kind (1 JSON / 2 stereo float PCM), uint32 length, payload.
// JSON stdout: request/response or asynchronous error. No network or recording.
sealed class PcmRing : IWaveProvider {
 readonly object gate=new object(); readonly byte[] bytes; int read,write,count;
 public long Received,Consumed,Underruns,Overflows;public int MaxReadFrames; public int Prebuffer;
 public WaveFormat WaveFormat { get; private set; }
 public PcmRing(int rate,int prebuffer){WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(rate,2);bytes=new byte[rate*8];Prebuffer=prebuffer;}
 public int Buffered { get {lock(gate)return count;} }
 public void Add(byte[] data){if(data.Length%8!=0)throw new Exception("Invalid stereo float block.");lock(gate){Received+=data.Length/8;if(data.Length>bytes.Length)throw new Exception("PCM block too large.");if(count+data.Length>bytes.Length){Overflows++;read=write=count=0;}int n=Math.Min(data.Length,bytes.Length-write);Buffer.BlockCopy(data,0,bytes,write,n);Buffer.BlockCopy(data,n,bytes,0,data.Length-n);write=(write+data.Length)%bytes.Length;count+=data.Length;}}
 public int Read(byte[] target,int offset,int length){lock(gate){MaxReadFrames=Math.Max(MaxReadFrames,length/8);int take=Math.Min(count,length);int n=Math.Min(take,bytes.Length-read);Buffer.BlockCopy(bytes,read,target,offset,n);Buffer.BlockCopy(bytes,0,target,offset+n,take-n);read=(read+take)%bytes.Length;count-=take;Consumed+=take/8;if(take<length){Array.Clear(target,offset+take,length-take);Underruns++;}return length;}}
}
static class AudioHost {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer { MaxJsonLength=2097152 };
 static readonly object stdout=new object();static IWavePlayer player;static PcmRing ring;static MMDevice endpoint;static bool started,panelOpen;static string backend="none";static int rate,driverFrames,bridgeFrames;static string failure;
 static string S(Dictionary<string,object> d,string key,string fallback=""){object v;return d.TryGetValue(key,out v)?Convert.ToString(v):fallback;}
 static int N(Dictionary<string,object> d,string key,int fallback){object v;return d.TryGetValue(key,out v)?Convert.ToInt32(v):fallback;}
 static void Reply(object value){lock(stdout){Console.Out.WriteLine(Json.Serialize(value));Console.Out.Flush();}}
 static object Devices(){var devices=new List<object>();var errors=new List<string>();try{using(var e=new MMDeviceEnumerator()){foreach(var d in e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)){devices.Add(new { api="wasapi",id=d.ID,name=d.FriendlyName });d.Dispose();}}}catch(Exception e){errors.Add("WASAPI: "+e.Message);}try{foreach(string name in AsioOut.GetDriverNames())devices.Add(new {api="asio",id=name,name=name});}catch(Exception e){errors.Add("ASIO: "+e.Message);}try{foreach(var d in DirectSoundOut.Devices)devices.Add(new {api="directsound",id=d.Guid.ToString(),name=d.Description});}catch(Exception e){errors.Add("DirectSound: "+e.Message);}try{for(int i=0;i<WaveOut.DeviceCount;i++)devices.Add(new {api="mme",id=i.ToString(),name=WaveOut.GetCapabilities(i).ProductName});}catch(Exception e){errors.Add("MME: "+e.Message);}return new {devices=devices,errors=errors};}
 static void Close(){var old=player;player=null;started=false;if(old!=null){try{old.Stop();}finally{old.Dispose();}}if(endpoint!=null){endpoint.Dispose();endpoint=null;}ring=null;backend="none";}
 static object Configure(Dictionary<string,object> d){Close();failure=null;backend=S(d,"backend");rate=N(d,"sampleRate",48000);bridgeFrames=N(d,"bridgeFrames",512);int latency=N(d,"latencyMs",30),channel=N(d,"channelOffset",0);string device=S(d,"deviceId");
 if(!new[]{44100,48000,88200,96000}.Contains(rate)||!new[]{256,512,1024,2048,4096}.Contains(bridgeFrames)||latency<10||latency>500||channel<0||channel>126)throw new Exception("Invalid audio configuration.");
 ring=new PcmRing(rate,Math.Max(bridgeFrames*2,rate*latency*2/1000)*8);driverFrames=0;
 try{
 if(backend=="wasapi-shared"||backend=="wasapi-exclusive"){using(var e=new MMDeviceEnumerator())endpoint=device==""?e.GetDefaultAudioEndpoint(DataFlow.Render,Role.Console):e.GetDevice(device);player=new WasapiOut(endpoint,backend=="wasapi-shared"?AudioClientShareMode.Shared:AudioClientShareMode.Exclusive,true,latency);player.Init(ring);}
 else if(backend=="asio"){if(device==""||!AsioOut.GetDriverNames().Contains(device))throw new Exception("Select an installed ASIO driver.");var asio=new AsioOut(device);player=asio;if(channel+2>asio.DriverOutputChannelCount)throw new Exception("Selected stereo output pair is unavailable.");if(!asio.IsSampleRateSupported(rate))throw new Exception("ASIO driver does not support this sample rate.");asio.ChannelOffset=channel;asio.AutoStop=false;asio.Init(ring);driverFrames=asio.FramesPerBuffer;ring.Prebuffer=Math.Max(bridgeFrames*2,driverFrames*2)*8;}
 else if(backend=="directsound"){player=new DirectSoundOut(device==""?Guid.Empty:new Guid(device),latency);player.Init(new WaveFloatTo16Provider(ring));}
 else if(backend=="mme"){int index=device==""?-1:int.Parse(device);if(index < -1||index>=WaveOut.DeviceCount)throw new Exception("MME device unavailable.");player=new WaveOutEvent {DeviceNumber=index,DesiredLatency=latency,NumberOfBuffers=3};player.Init(new WaveFloatTo16Provider(ring));}
 else throw new Exception("Unknown native audio backend.");
 player.PlaybackStopped+=(sender,args)=>{if(args.Exception!=null){failure=args.Exception.Message;Reply(new {eventType="audioError",error=failure});}};
 if(player is AsioOut)((AsioOut)player).DriverResetRequest+=(sender,args)=>{failure="ASIO driver changed its settings. Reapply Audio preferences.";if(!panelOpen)Reply(new {eventType="audioError",error=failure});};
 return Stats();
 }catch{Close();throw;}}
 static object Stats(){return new {backend=backend,sampleRate=rate,bridgeFrames=bridgeFrames,driverFrames=driverFrames,started=started,queuedFrames=ring==null?0:ring.Buffered/8,receivedFrames=ring==null?0:ring.Received,consumedFrames=ring==null?0:ring.Consumed,maxReadFrames=ring==null?0:ring.MaxReadFrames,underruns=ring==null?0:ring.Underruns,overflows=ring==null?0:ring.Overflows,error=failure};}
 static object Command(Dictionary<string,object> d){switch(S(d,"command")){case "devices":return Devices();case "configure":return Configure(d);case "stats":return Stats();case "close":Close();return true;case "controlPanel":if(!(player is AsioOut))throw new Exception("Apply an ASIO device first.");panelOpen=true;try{((AsioOut)player).ShowControlPanel();}finally{panelOpen=false;}return true;default:throw new Exception("Unknown audio command.");}}
 static void SelfTest(){var r=new PcmRing(48000,0);byte[] data=new byte[80];for(int i=0;i<data.Length;i++)data[i]=(byte)i;r.Add(data);byte[] result=new byte[96];if(r.Read(result,0,result.Length)!=96||!result.Take(80).SequenceEqual(data)||result.Skip(80).Any(b=>b!=0)||r.Underruns!=1||r.Consumed!=10)throw new Exception("Ring test failed.");r.Add(data);r.Add(data);byte[] more=new byte[160];r.Read(more,0,160);if(!more.SequenceEqual(data.Concat(data)))throw new Exception("FIFO test failed.");try{r.Add(new byte[3]);throw new Exception("Invalid block accepted.");}catch(Exception e){if(e.Message=="Invalid block accepted.")throw;}Reply(new {ok=true,tests=3});}
 [STAThread]static int Main(string[] args){Console.OutputEncoding=new UTF8Encoding(false);if(args.Contains("--self-test")){SelfTest();return 0;}var input=new BinaryReader(Console.OpenStandardInput());try{while(true){int kind;try{kind=input.ReadByte();}catch(EndOfStreamException){break;}int length=input.ReadInt32();if(length<0||length>2097152)throw new Exception("Invalid packet size.");byte[] data=input.ReadBytes(length);if(data.Length!=length)break;if(kind==2){if(ring!=null&&failure==null){ring.Add(data);if(!started&&ring.Buffered>=ring.Prebuffer){player.Play();started=true;}}}else if(kind==1){int id=0;try{var d=Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(data));id=N(d,"id",0);Reply(new {id=id,ok=true,value=Command(d)});}catch(Exception e){Reply(new {id=id,ok=false,error=e.Message});}}else throw new Exception("Invalid packet type.");}}catch(Exception e){Reply(new {eventType="audioError",error=e.Message});return 1;}finally{Close();}return 0;}
}
