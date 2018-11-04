module Realtime

open System
open PriceProxy


[<EntryPoint>]
let main argv =
    newProxy ()
    |> definePrice "ticker1" { MinValue= 1.0; MaxValue = 3.0; MinInterval = 1000.0; MaxInterval = 2000.0}
    |> definePrice "ticker2" { MinValue= 100.0; MaxValue = 300.0; MinInterval = 1000.0; MaxInterval = 4000.0}
    |> subscribeAll (fun price -> printfn "%s: %f" price.Ticker price.Value)
    |> start

    Console.ReadLine () |> ignore
    0 // return an integer exit code



