define %String* @Microsoft__Quantum__Testing__QIR__TestStrings__body(i64 %a, i64 %b) {
entry:
  %0 = call %String* @__quantum__rt__string_create(i32 5, [0 x i8]* bitcast ([6 x i8]* @0 to [0 x i8]*))
  %1 = call %String* @__quantum__rt__int_to_string(i64 %a)
  %2 = call %String* @__quantum__rt__string_concatenate(%String* %0, %String* %1)
  call void @__quantum__rt__string_unreference(%String* %0)
  call void @__quantum__rt__string_unreference(%String* %1)
  %3 = call %String* @__quantum__rt__string_create(i32 5, [0 x i8]* bitcast ([6 x i8]* @1 to [0 x i8]*))
  %x = call %String* @__quantum__rt__string_concatenate(%String* %2, %String* %3)
  call void @__quantum__rt__string_unreference(%String* %2)
  call void @__quantum__rt__string_unreference(%String* %3)
  %4 = call %String* @__quantum__rt__string_create(i32 7, [0 x i8]* bitcast ([8 x i8]* @2 to [0 x i8]*))
  %5 = add i64 %a, %b
  %6 = call %String* @__quantum__rt__int_to_string(i64 %5)
  %y = call %String* @__quantum__rt__string_concatenate(%String* %4, %String* %6)
  call void @__quantum__rt__string_unreference(%String* %4)
  call void @__quantum__rt__string_unreference(%String* %6)
  call void @__quantum__rt__string_reference(%String* %y)
  %7 = call %String* @__quantum__rt__string_create(i32 16, [0 x i8]* bitcast ([17 x i8]* @3 to [0 x i8]*))
  %8 = call %String* @__quantum__rt__double_to_string(double 1.200000e+00)
  %9 = call %String* @__quantum__rt__string_concatenate(%String* %7, %String* %8)
  call void @__quantum__rt__string_unreference(%String* %7)
  call void @__quantum__rt__string_unreference(%String* %8)
  %10 = call %String* @__quantum__rt__string_create(i32 6, [0 x i8]* bitcast ([7 x i8]* @4 to [0 x i8]*))
  %11 = call %String* @__quantum__rt__string_concatenate(%String* %9, %String* %10)
  call void @__quantum__rt__string_unreference(%String* %9)
  call void @__quantum__rt__string_unreference(%String* %10)
  %12 = call %String* @__quantum__rt__bool_to_string(i1 true)
  %13 = call %String* @__quantum__rt__string_concatenate(%String* %11, %String* %12)
  call void @__quantum__rt__string_unreference(%String* %11)
  call void @__quantum__rt__string_unreference(%String* %12)
  %14 = call %String* @__quantum__rt__string_create(i32 7, [0 x i8]* bitcast ([8 x i8]* @5 to [0 x i8]*))
  %15 = call %String* @__quantum__rt__string_concatenate(%String* %13, %String* %14)
  call void @__quantum__rt__string_unreference(%String* %13)
  call void @__quantum__rt__string_unreference(%String* %14)
  %16 = load i2, i2* @PauliX
  %17 = call %String* @__quantum__rt__pauli_to_string(i2 %16)
  %18 = call %String* @__quantum__rt__string_concatenate(%String* %15, %String* %17)
  call void @__quantum__rt__string_unreference(%String* %15)
  call void @__quantum__rt__string_unreference(%String* %17)
  %19 = call %String* @__quantum__rt__string_create(i32 8, [0 x i8]* bitcast ([9 x i8]* @6 to [0 x i8]*))
  %20 = call %String* @__quantum__rt__string_concatenate(%String* %18, %String* %19)
  call void @__quantum__rt__string_unreference(%String* %18)
  call void @__quantum__rt__string_unreference(%String* %19)
  %21 = load %Result*, %Result** @ResultOne
  %22 = call %String* @__quantum__rt__result_to_string(%Result* %21)
  %23 = call %String* @__quantum__rt__string_concatenate(%String* %20, %String* %22)
  call void @__quantum__rt__string_unreference(%String* %20)
  call void @__quantum__rt__string_unreference(%String* %22)
  %24 = call %String* @__quantum__rt__string_create(i32 8, [0 x i8]* bitcast ([9 x i8]* @7 to [0 x i8]*))
  %25 = call %String* @__quantum__rt__string_concatenate(%String* %23, %String* %24)
  call void @__quantum__rt__string_unreference(%String* %23)
  call void @__quantum__rt__string_unreference(%String* %24)
  %26 = call %BigInt* @__quantum__rt__bigint_create_i64(i64 1)
  %27 = call %String* @__quantum__rt__bigint_to_string(%BigInt* %26)
  %28 = call %String* @__quantum__rt__string_concatenate(%String* %25, %String* %27)
  call void @__quantum__rt__string_unreference(%String* %25)
  call void @__quantum__rt__string_unreference(%String* %27)
  %29 = call %String* @__quantum__rt__string_create(i32 7, [0 x i8]* bitcast ([8 x i8]* @8 to [0 x i8]*))
  %30 = call %String* @__quantum__rt__string_concatenate(%String* %28, %String* %29)
  call void @__quantum__rt__string_unreference(%String* %28)
  call void @__quantum__rt__string_unreference(%String* %29)
  %31 = load %Range, %Range* @EmptyRange
  %32 = insertvalue %Range %31, i64 0, 0
  %33 = insertvalue %Range %32, i64 1, 1
  %34 = insertvalue %Range %33, i64 3, 2
  %35 = call %String* @__quantum__rt__range_to_string(%Range %34)
  %i = call %String* @__quantum__rt__string_concatenate(%String* %30, %String* %35)
  call void @__quantum__rt__string_unreference(%String* %30)
  call void @__quantum__rt__string_unreference(%String* %35)
  %36 = call %String* @__quantum__rt__string_create(i32 7, [0 x i8]* bitcast ([8 x i8]* @9 to [0 x i8]*))
  call void @__quantum__rt__string_reference(%String* %x)
  %37 = call %String* @__quantum__rt__string_concatenate(%String* %36, %String* %x)
  call void @__quantum__rt__string_unreference(%String* %36)
  call void @__quantum__rt__string_unreference(%String* %x)
  call void @__quantum__rt__string_unreference(%String* %x)
  call void @__quantum__rt__string_unreference(%String* %y)
  call void @__quantum__rt__string_unreference(%String* %y)
  call void @__quantum__rt__bigint_unreference(%BigInt* %26)
  call void @__quantum__rt__string_unreference(%String* %i)
  ret %String* %37
}
