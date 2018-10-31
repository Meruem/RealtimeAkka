module Realtime

open System.Timers
open System


type PriceRecord = {
    Ticker : string
    Value : float
}

type PriceConfig = {
    MinValue : float
    MaxValue : float
    MinInterval : float
    MaxInterval : float
}

type PriceProxy = {
    TickerSubs : Map<string, (PriceRecord -> Unit) list>
    AllSubs : (PriceRecord -> Unit) list
    PriceConfigs : Map<string, PriceConfig>
    NextTicks : Map<string, DateTime>
    Timer : Timer
}

let rnd = new Random()

let subscribe ticker handler proxy = 
    { proxy with TickerSubs = proxy.TickerSubs.Add (ticker, handler :: proxy.TickerSubs.[ticker])}

let subscribeAll handler proxy = 
    { proxy with AllSubs = handler :: proxy.AllSubs}

let scheduleTick ticker config proxy = 
    let nextTick = rnd.NextDouble() * (config.MaxInterval - config.MinInterval) + config.MinInterval 
    {proxy with NextTicks = proxy.NextTicks.Add (ticker, DateTime.Now.AddMilliseconds (nextTick))}

let definePrice ticker config proxy =
    { proxy with PriceConfigs = proxy.PriceConfigs.Add (ticker, config)}   

let createPrice ticker cfg =
    let value = rnd.NextDouble() * (cfg.MaxValue - cfg.MinValue) + cfg.MinValue
    { Ticker= ticker; Value = value}

let start proxy =
    let proxy' = 
        proxy.PriceConfigs 
        |> Map.fold (fun acc ticker cfg -> acc |> scheduleTick ticker cfg ) proxy
    
    proxy'.Timer.Elapsed.Add (fun evArgs -> 
        proxy'.NextTicks 
        |> Map.filter (fun _ time -> time <= DateTime.Now) 
        |> Map.iter (fun ticker _ -> 
            let invokeHandler handler = handler (createPrice ticker proxy'.PriceConfigs.[ticker])
            proxy'.AllSubs |> List.iter invokeHandler
            if proxy'.TickerSubs.ContainsKey ticker then proxy'.TickerSubs.[ticker] |> List.iter invokeHandler))
        
    proxy'.Timer.Start()

let newProxy () = {
    TickerSubs = Map.empty
    AllSubs = []
    PriceConfigs = Map.empty
    NextTicks = Map.empty
    Timer = new Timer(1000.0) }

[<EntryPoint>]
let main argv =
    newProxy ()
    |> definePrice "ticker1" { MinValue= 1.0; MaxValue = 3.0; MinInterval = 1000.0; MaxInterval = 2000.0}
    |> definePrice "ticker2" { MinValue= 100.0; MaxValue = 300.0; MinInterval = 1000.0; MaxInterval = 4000.0}
    |> subscribeAll (fun price -> printfn "%s: %f" price.Ticker price.Value)
    |> start

    Console.ReadLine () |> ignore
    0 // return an integer exit code



