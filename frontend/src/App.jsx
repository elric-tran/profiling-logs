import React from 'react'
import ClearButton from './components/ClearButton'
import Footer from './components/Footer'
import CoffeePanel from './components/CoffeePanel'
import ResultsIndexEnhancer from './components/ResultsIndexEnhancer'

export default function App({ options }) {
  const isResultsIndex = !!options.IsResultsIndex;

  return (
    <>
      {isResultsIndex && options.EnableClearCacheButton && (
        <ClearButton clearPath={options.ClearPath || '/profiler/clear-cache'} />
      )}
      {isResultsIndex && (
        <ResultsIndexEnhancer enableMethodColumn={!!options.EnableHttpMethodColumn} />
      )}
      {isResultsIndex && (
        <CoffeePanel qrData={options.CoffeeQrData} url={options.CoffeeUrl} />
      )}
      <Footer />
    </>
  )
}
